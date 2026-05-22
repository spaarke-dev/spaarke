# Task Index — Spaarke AI Platform Unification R3

> **Last Updated**: 2026-05-20
> **Total Tasks**: 36 (1 spike + 4 foundations + 7 Assistant + 6 Workspace + 6 Context + 2 backend conditional + 4 verify + 5 deploy/smoke + 1 wrap-up)
> **Project Status**: Not started
> **Status Legend**: 🔲 not-started · 🔄 in-progress · 🚫 blocked · ✅ completed · ⏭️ skipped

---

## Quick Status

| Phase | Tasks | Status |
|---|---|---|
| Phase 0 (Spike) | 1 (001) | ✅ |
| Phase A (Foundations) | 4 (010-013) | ✅ |
| Phase B (Assistant) | 7 (020-026) | 🔲 |
| Phase C (Workspace) | 6 (030-035) | 🔲 |
| Phase D (Context) | 6 (040-045) | 🔲 |
| Phase E (Backend, conditional) | 2 (050, 051) | ✅ — Phase E COMPLETE (per spike 001 decision; memo: notes/spikes/001-fr07-attachments-payload.md) |
| Phase F (Verification) | 5 (060-063, 065) | ✅ all 5 complete — NFR-09 fix landed in task 065 |
| Phase G (Deploy + Smoke) | 5 (070-074) | 🔄 (070 ✅ deployed; 071-074 in-progress — operator-driven smoke) |
| Phase H (Wrap-up) | 1 (090) | 🔲 |

---

## Full Task Listing

| ID | Title | Phase | Status | Dependencies | Blocks | Parallel | Rigor | Est. h |
|----|-------|-------|--------|--------------|--------|----------|-------|--------|
| 001 | Spike: FR-07 attachments payload verification | 0 (Spike) | ✅ | none | 010-013, 026, 050, 051 | — (serial) | STANDARD | 1 |
| 010 | Create `<PaneHeader>` shared component | A (Foundations) | ✅ | 001 | 021, 022, 030, 032, 040 | A | FULL | 2 |
| 011 | Configure `MAX_WORKSPACE_TABS = 8` + FIFO | A (Foundations) | ✅ | 001 | 032 | A | FULL | 2 |
| 012 | Lift `ActionCard` to `@spaarke/ui-components` (or verify shared) | A (Foundations) | ✅ | 001 | 041 | A | FULL | 3 |
| 013 | Error-only telemetry helpers | A (Foundations) | ✅ | 001 | 022, 035 | A | STANDARD | 2 |
| 020 | WelcomePanel chrome trim | B (Assistant) | ✅ | 001, 010-013 | — | B | FULL | 2 |
| 021 | ConversationPane → PaneHeader | B (Assistant) | ✅ | 010 | 022 | B (serial w/ 022 on ConversationPane.tsx) | FULL | 2 |
| 022 | HistoryOverlay component + wiring | B (Assistant) | ✅ | 010, 021 | — | B (serial w/ 021) | FULL | 3 |
| 023 | SprkChatInput editable on cold load | B (Assistant) | ✅ | 010 | 025, 026 | B (serial w/ 025 on SprkChat.tsx) | FULL | 2 |
| 024 | `useChatFileAttachment` hook + lazy extraction | B (Assistant) | ✅ | 010 | 025, 026 | B | FULL | 4 |
| 025 | SprkChat toolbar restructure (+, remove Word) | B (Assistant) | ✅ | 023, 024 | 026 | B (serial w/ 023) | FULL | 3 |
| 026 | Wire attachments into chat send payload | B (Assistant) | ✅ | 001, 024, 025 | — | — (serial — gated by spike) | FULL | 3 |
| 030 | WorkspacePane → PaneHeader + embed LegalWorkspace | C (Workspace) | ✅ | 010 | 031, 032, 034 | C (serial on WorkspacePane.tsx) | FULL | 4 |
| 031 | Delete `WorkspaceLandingWidget.tsx` | C (Workspace) | ✅ | 030 | — | C (serial w/ 030) | STANDARD | 1 |
| 032 | `WorkspacePaneMenu` Dropdown component | C (Workspace) | ✅ | 010, 011, 030 | — | C (serial on WorkspacePane.tsx) | FULL | 4 |
| 033 | `WorkspaceLayoutWizard.templateFilter` prop | C (Workspace) | ✅ | 001 | — | C | STANDARD | 2 |
| 034 | Daily Briefing section + `useDailyBriefing` hook | C (Workspace) | ✅ | 030 | 035 | C | FULL | 4 |
| 035 | Daily Briefing 429 + empty state | C (Workspace) | ✅ | 013, 034 | — | C (depends on 034) | FULL | 3 |
| 040 | ContextPaneController → PaneHeader + "Get Started" label | D (Context) | ✅ | 010 | 042 | D (serial on ContextPaneController.tsx) | FULL | 3 |
| 041 | `GetStartedCardsWidget` (7 cards, 2-col grid) | D (Context) | ✅ | 012 | 042 | D | FULL | 3 |
| 042 | Register widget + welcome-stage swap | D (Context) | ✅ | 040, 041 | — | D (serial on ContextPaneController.tsx) | FULL | 3 |
| 043 | Wizard widget wrappers (CreateProject, FindSimilar) | D (Context) | ✅ | 001 | — | D | FULL | 2 |
| 044 | Analysis Builder intents (email-compose, meeting-schedule) | D (Context) | ✅ | 001 | — | D | STANDARD | 2 |
| 045 | `AssignWorkWizardLauncher` (Xrm.Navigation) | D (Context) | ✅ | 001 | — | D | FULL | 2 |
| 050 | Extend ChatEndpoints with attachments[] (CONDITIONAL) | E (Backend) | ✅ | 001 | 051, 026 | E | FULL | 3 |
| 051 | BFF unit tests for attachments[] payload (CONDITIONAL) | E (Backend) | ✅ | 050 | — | E (serial w/ 050) | STANDARD | 2 |
| 060 | Auth audit (no token snapshots, all via `authenticatedFetch`) | F (Verification) | ✅ | All B/C/D | 070 | F | STANDARD | 2 |
| 061 | Bundle-size verification (<250 KB gzip delta vs R2) | F (Verification) | ✅ | All B/C/D | 070 | F | STANDARD | 2 |
| 062 | Dark-mode token audit (no hex/rgba in new code) | F (Verification) | ✅ | All B/C/D | 070 | F | STANDARD | 2 |
| 063 | Backwards-compat verification (standalone LW + persistence) | F (Verification) | ✅ | C tasks | 070 | F | STANDARD | 2 |
| 065 | Extend SessionPersistence with workspace tabs[] (NFR-09 fix) | F (Verification — remediation) | ✅ | 011, 030, 050, 063 | 070 | — (serial) | FULL | 5 |
| 066 | Redeploy WorkspaceLayoutWizard + update Deploy-WizardCodePages.ps1 (Bug 3 fix from smoke) | F (Verification — remediation) | ✅ | 032, 033, 070 | 072 | — (serial) | STANDARD | 0.5 |
| 067 | Hoist workspace section registry to @spaarke/ui-components (Bug 2 fix + ADR-012 compliance) | F (Verification — remediation) | ✅ | 030, 034, 070 | 072 | — (serial) | FULL | 12 |
| 068 | Smoke remediation: Bug 1 (chat box) + UX-A (Recent Conversations) + UX-B (all wizards as popups) | F (Verification — remediation) | ✅ | 020, 021, 023, 025, 042, 067, 070 | 071, 072, 073 | — (serial) | FULL | 2.5 |
| 069 | Daily Briefing as SpaarkeAi Home tab default content (Option Z minimum scope; Bug 2 visual fix) | F (Verification — remediation) | ✅ | 034, 067, 068, 070 | 072 | — (serial) | FULL | 5 |
| 081 | WorkspacePaneMenu config-ready fetch fix (smoke remediation round 2) | F (Verification — remediation) | ✅ | 032, 070 | 072 | — (serial) | STANDARD | 1 |
| 082 | SprkChat UX fixes — input focus restore + slash menu outside-click (smoke remediation round 2) | F (Verification — remediation) | ✅ | 023, 025, 070 | 071 | — (serial) | STANDARD | 1 |
| 083 | BFF resilience: missing Dataverse manifest + empty Daily Briefing payload (smoke remediation round 2 — unblocks chat + Daily Briefing in dev) | F (Verification — remediation) | ✅ | 050, 070 | 071, 072 | — (serial) | STANDARD | 1.5 |
| 084 | WorkspacePaneMenu: replace task-081 parallel implementation with reused LegalWorkspace `useWorkspaceLayouts` hook (Round 4 Fix 1 — workspaces dropdown still empty after 081) | F (Verification — remediation) | ✅ | 032, 081 | 072 | — (serial) | STANDARD | 2 |
| 085 | Get Started wizard launchers: hoist LegalWorkspace's exact navigateTo handlers to `@spaarke/ui-components` and reuse from SpaarkeAi ContextPaneController (Round 4 Fix 2 — wizards opened from Context-pane cards now route through the proven LegalWorkspace call shape; removes parallel `launchCodePagePopup` + `launchAssignWorkWizard` helpers) | F (Verification — remediation) | ✅ | 042, 044, 045, 068 | 073 | — (serial) | STANDARD | 3 |
| 086 | Daily Briefing functional in SpaarkeAi Home tab (Round 4 Fix 3 — Hoist optional `loadNotificationContext` callback into shared `useDailyBriefing` hook + `createDailyBriefingRegistration` factory; Copy standalone Daily Briefing Code Page's Xrm.WebApi `appnotification` query + payload assembly into SpaarkeAi-local `notificationContextLoader.ts`; wire into `WorkspaceHomeTab.tsx` so the narrate endpoint receives a populated payload — same data path as the standalone, so the embedded section returns real AI bullets on cold load instead of the empty-state UI; LegalWorkspace shim untouched preserves FR-25) | F (Verification — remediation) | ✅ | 034, 035, 069, 083 | 072 | — (serial) | FULL | 4 |
| 087 | Workspace dropdown opens chosen workspace via embedded `LegalWorkspaceApp` (Round 4 Fix 4, Option A — Add `initialWorkspaceId` + `embedded` props to `LegalWorkspaceApp.tsx`; create `WorkspaceLayoutWidget` that embeds the full LegalWorkspace experience as a single workspace pane tab; wire `WorkspacePaneMenu.handleLayoutSelect` to dispatch `widget_load` so the existing tab pipeline opens it; add `active_widget_changed` event + `onActiveTabChange` manager callback as foundation signal infrastructure for future Assistant/Context pane coordination — no consumers wired in this task; @hello-pangea/dnd added to SpaarkeAi as a transitive dep of LegalWorkspace's SmartToDo; SpaarkeAi gzip +92 KB; standalone LegalWorkspace bundle byte-identical so FR-25/NFR-10 preserved) | F (Verification — remediation) | ✅ | 067, 084, 086 | 072 | — (serial) | FULL | 5 |
| 089 | WorkspacePaneMenu UX cleanup — remove `Open` + `Home` sections (operator feedback 2026-05-21: redundant with the visible tab bar + Home concept retired); rename `Switch Workspace` MenuGroupHeader to `Select Workspace`; add `Manage workspaces` stub entry (onClick logs placeholder — full side pane in task 093); keep `Edit current workspace` (operator did not request removal). Trims styles + icon imports unused by removed sections. `WorkspacePaneMenuProps` callbacks marked optional (back-compat with `WorkspacePane.tsx` call site, no `WorkspacePane.tsx` edit needed). SpaarkeAi 893.43 KB gzip (−0.32 KB vs task 088). LegalWorkspace untouched (FR-25). Deployed to spaarkedev1. | F (Verification — remediation) | ✅ | 088 | — | — (serial) | STANDARD | 1 |
| 091 | Workspace builder UX iteration (Round 5 — operator smoke). **Fix 1**: empty slots in the layout canvas are now save-tolerant — the wizard already only required a workspace name to advance; `buildDynamicWorkspaceConfig` now silently skips empty section IDs (was logging a warning) so user-removed slots render as empty space instead of console noise. **Fix 2**: each filled slot in the Arrange Sections step now renders a small `DismissRegular` X button in its top-right corner — clicking removes the section from the slot and returns it to the unassigned pool; visible on hover and on focus (a11y); does NOT interfere with HTML5 drag (stopPropagation + draggable=false). **Fix 3**: added `Pin to Start` checkbox to Step 3's inline row (next to `Set default`) with `PinRegular` icon; default unchecked; persistence stubbed via `sessionStorage` key `spaarke:workspace:pinned-layout-id` (only one pinned at a time). BFF DTO (`CreateWorkspaceLayoutRequest` / `UpdateWorkspaceLayoutRequest`) NOT modified — task 092 will add `IsPinned` field + `sprk_workspacelayout.sprk_ispinned` column + wire SpaarkeAi auto-open. Wizard's `__dialogResult` now carries the `pinToStart` flag for callers that want to react immediately. FR-25 preserved — standalone LegalWorkspace passes neither `templateFilter` nor `pinToStart`; both default gracefully (LegalWorkspace bundle 569.31 KB gzip vs 569.30 baseline = +10 bytes from the empty-slot guard's two new lines; SpaarkeAi 893.44 KB gzip vs 893.43 baseline = +10 bytes). Wizard bundle 427.58 KB gzip. Deployed to spaarkedev1 (sprk_workspacelayoutwizard + sprk_corporateworkspace + sprk_spaarkeai all updated). | F (Verification — remediation) | ✅ | 033, 088 | 092 | — (serial) | FULL | 3 |
| 088 | Embed wiring fixes (Round 4 Fix 4.1 — three bugs from task 087 operator smoke). **Bug 1**: each embedded LegalWorkspaceApp tab showed the same "last-active" workspace because `useWorkspaceLayouts` cache-hydrated from a sessionStorage key shared across all tabs. Added new `embedded?: boolean` option to `useWorkspaceLayouts` (backwards-compat overload) — when true, hook SKIPS sessionStorage reads + writes so each tab honours its own `initialWorkspaceId` deep-link only. Wired through `WorkspaceGrid` and `LegalWorkspaceApp`. **Bug 2**: all workspace tabs showed title "Workspace" (registry metadata's generic label) instead of the chosen layout name. `WorkspacePaneMenu.handleLayoutSelect` now sets `displayName: layoutName` at the TOP LEVEL of the `widget_load` event (not just `widgetData.layoutName`) so `WorkspacePane.tsx`'s displayName-precedence ladder picks the per-instance label. **Bug 3**: document preview + other actions inside the embedded tree threw "[LegalWorkspace] Runtime config not initialized" because LegalWorkspace has its OWN `runtimeConfig` singleton (distinct from SpaarkeAi's) and SpaarkeAi's `main.tsx` only initialized SpaarkeAi's. Option A (dual-init): re-exported `setRuntimeConfig as setLegalWorkspaceRuntimeConfig` from LegalWorkspace's barrel; SpaarkeAi's bootstrap calls it with the SAME `IRuntimeConfig` after its own `setRuntimeConfig(config)`. Both singletons remain distinct in-process instances holding equivalent values. Standalone LegalWorkspace untouched at the runtime entry point (its own `main.tsx → App.tsx` still calls its own internal `setRuntimeConfig`); FR-25 / NFR-10 preserved (gzip 569.30 KB vs 569.22 KB baseline = +80 bytes of structural code for new option object normalization in `useWorkspaceLayouts`; standalone code paths never traverse the new branches). SpaarkeAi bundle 893.75 KB gzip (−0.16 KB vs task 087 baseline). Deployed to spaarkedev1. | F (Verification — remediation) | ✅ | 087 | 072 | — (serial) | FULL | 2 |
| 092 | Tab pinning + auto-open pinned workspaces on SpaarkeAi startup (Round 5 — operator UX iteration extending task 091). **Storage**: replaced task 091's single-pin `sessionStorage` stub with a multi-pin `localStorage` list keyed `spaarke:workspace:pinned-list` — array of `{ layoutId, layoutName }` records. New utility `src/solutions/SpaarkeAi/src/services/pinnedWorkspaces.ts` exports `getPinnedWorkspaces()` / `isPinned()` / `pinWorkspace()` / `unpinWorkspace()` with try/catch around every localStorage call (private browsing / quota safe). Wizard's `App.tsx` updated to the same shape (BFF DTO still NOT modified; no user-preferences endpoint exists in the codebase). **Auto-open**: new mount effect in `WorkspacePane.tsx` reads the pinned list AFTER auth ready (`isAuthenticated` guard), filters out workspaces that are already open (duplicate guard by `widgetData.layoutId`), and dispatches `widget_load` for each remaining pin — same pipeline `WorkspacePaneMenu.handleLayoutSelect` uses, so pinned tabs hydrate via the existing tab manager + `WorkspaceLayoutWidget` chain. Home tab still installs as default; pinned tabs open IN ADDITION (user can close Home if they don't want it). One-shot per mount via `autoOpenedPinsRef` so token refresh doesn't re-stack tabs. **Pin icon on tabs**: `WorkspaceTabManagerComponent.tsx` now renders a `PinRegular` / `PinFilled` button next to the close button on every workspace tab (`widgetType === "workspace"` + `widgetData.layoutId` present, `kind === "widget"`). Home tab + non-workspace widget tabs (Create Matter wizard etc.) do NOT show the pin icon. Click toggles the localStorage list + updates local React `pinnedIds` Set so the icon flips synchronously. Pinned state uses `tokens.colorBrandForeground1` for visual prominence; unpinned matches close-button subtle treatment. Tooltips read "Pin {name} to start" / "Unpin {name} from start"; `aria-pressed` reflects state. **Build**: SpaarkeAi 894.21 KB gzip (+0.77 KB vs task 091 baseline 893.44); wizard 427.76 KB gzip (+0.18 KB vs 427.58). LegalWorkspace untouched (FR-25 / NFR-10). **TODO documented**: BFF user-preferences endpoint (cross-device sync) deferred to next review — no such endpoint exists in `Sprk.Bff.Api` today (the `UserPreferences` records in `AiIntentClassificationSchema.cs` / `PlaybookRunContext.cs` are AI-pipeline payloads, not a generic preferences surface). Deployed to spaarkedev1 (sprk_spaarkeai + sprk_workspacelayoutwizard + the 7 other wizard pages refreshed by the all-wizards deploy script). | F (Verification — remediation) | ✅ | 088, 091 | — | — (serial) | FULL | 3 |
| 095 | Context pane tool selector + Semantic Search Criteria (Round 5 — operator UX iteration). New PaneHeader rightSlot dropdown (Fluent v9 Menu mirroring `WorkspacePaneMenu` pattern) exposes two Context tools: **Quick Start** (default — existing `GetStartedCardsWidget` 7-card grid) and **Semantic Search** (new `SemanticSearchCriteriaTool` — in-pane Domain `<Dropdown>` + AI query `<Textarea>` + optional from/to date `<Input type="date">` + primary Search button). Search button launches `sprk_semanticsearch` via `Xrm.Navigation.navigateTo` with `query` + `domain` + `dateFrom` + `dateTo` URL params (80% × 80% modal). **Files added**: `src/solutions/SpaarkeAi/src/hooks/useContextTool.ts` (typed localStorage-persisted tool selection — same try/catch posture as `usePaneCollapse.ts` from task 094); `src/solutions/SpaarkeAi/src/components/context/ContextPaneMenu.tsx` (Fluent v9 Menu dropdown — `MenuTrigger` Button + `MenuPopover` + `MenuList` + `MenuGroupHeader` + two MenuItems with active-checkmark marker); `src/solutions/SpaarkeAi/src/components/context/SemanticSearchCriteriaTool.tsx` (in-pane criteria editor + Xrm frame-walk modal launcher; criteria state persisted in `spaarke:context:semantic-search-criteria`). **Files modified**: `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` — wires `useContextTool()` hook + composes `<ContextPaneMenu>` in PaneHeader rightSlot alongside existing stage label `<Text>` in a flex row; `renderContent()` updated so `selectedTool` is the source of truth (`semantic-search` → always renders the criteria tool; `quick-start` + no server-driven `activeWidget` → renders `<GetStartedCardsWidget/>` on any stage — uniform fix for the "pane goes blank after modal close" bug operator reported for the playbook-modal flow). Persisted selection in `localStorage["spaarke:context:selected-tool"]` is the same posture as task 094's `spaarke:panes:collapsed`. **Build**: SpaarkeAi 896.59 KB gzip (+1.55 KB vs task 094 baseline 895.04); LegalWorkspace 569.35 KB gzip — byte-identical to task 094 baseline (FR-25 / NFR-10 preserved); SemanticSearch Code Page untouched. ADR-012 (SpaarkeAi-local placement correct — `ContextToolId` union + storage namespace + SemanticSearch launch are solution-specific concerns), ADR-021 (Fluent v9 tokens only — no hex / rgba), ADR-022 (React 19 functional), ADR-025 (`@fluentui/react-icons` v9 — `ChevronDownRegular`, `AppsListRegular`, `SearchRegular`, `CheckmarkRegular`), ADR-028 (no auth surface change — criteria stay client-side until modal hand-off; `sprk_semanticsearch` has its own MSAL bootstrap). Deployed to spaarkedev1. | F (Verification — remediation) | ✅ | 094 | — | — (serial) | FULL | 3 |
| 094 | Per-pane collapse/expand on the SpaarkeAi three-pane shell (Round 5 — operator UX iteration). Operator request: click anywhere on a pane's header (but NOT on icon buttons inside the header's rightSlot) to toggle that pane's collapsed state; collapsed pane renders as a narrow vertical strip (~36px) with the pane's name rotated 90° (mirrors SmartToDo `KanbanBoard.tsx` column-header collapse pattern). All three panes (Assistant / Workspace / Context) can be collapsed simultaneously. State persists across browser sessions via `localStorage` key `spaarke:panes:collapsed` so a refresh restores the user's preference. **Files modified**: (1) `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/PaneHeader.tsx` — added optional `onCollapse?: () => void` + `expanded?: boolean` props; when wired, header becomes a `role="button"` with `tabIndex=0`, `aria-expanded`, Enter/Space keyboard handlers, and `cursor:pointer` hover treatment. The rightSlot wrapper applies `stopPropagation` on click + keydown so interactive children (Buttons, Dropdowns) don't bubble their events up to the header collapse handler. When `onCollapse` is undefined, ALL new code paths short-circuit — backwards compatible with the existing PaneHeader contract (LegalWorkspace standalone unchanged). (2) `src/client/shared/Spaarke.UI.Components/src/components/ThreePaneLayout/ThreePaneLayout.tsx` + `.types.ts` — added 6 optional props for externally-controlled collapse: `leftCollapsed` / `centerCollapsed` / `rightCollapsed` + `onToggleLeft` / `onToggleCenter` / `onToggleRight` + `centerPaneCollapseLabel`. The center pane is now collapsible (operator requested all three panes collapsible — base layout previously only supported left/right). Collapsed center renders as a 36px strip with rotated label; click / Enter / Space re-expands. Splitters hide when adjacent panes are collapsed. (3) `src/solutions/SpaarkeAi/src/hooks/usePaneCollapse.ts` (new, ~140 lines) — typed Set-based hook with `PaneId = 'assistant' \| 'workspace' \| 'context'` discriminated union; reads / writes `localStorage["spaarke:panes:collapsed"]` (try/catch-wrapped per task 092's posture); validates persisted IDs against `VALID_PANE_IDS` so a corrupted entry can't crash render. (4) `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` — wires `usePaneCollapse` + new `PaneCollapseContext` (so each pane can read its own collapsed state + toggle without prop-drilling); passes all 6 collapse props to `ThreePaneLayout`; collapse labels updated to bare pane names ("Assistant", "Workspace", "Context"). (5) `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — wires `onCollapse={handleHeaderCollapse}` + `expanded={isAssistantExpanded}` on its PaneHeader; History icon button onClick adds `e.stopPropagation()` as a defensive belt (PaneHeader's rightSlot container also stops propagation — belt-and-braces). (6) `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` — wires Workspace pane PaneHeader (WorkspacePaneMenu in rightSlot is already protected by PaneHeader's rightSlot stopPropagation guard). (7) `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` — wires Context pane PaneHeader (stage label in rightSlot is non-interactive Text, but the rightSlot guard still applies). **Reuse**: pattern lifted verbatim from `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` (lines 337-352 — `collapsedColumns: ReadonlySet<string>` state) and `src/solutions/LegalWorkspace/src/components/shared/KanbanBoard.tsx` (lines 132-156 — `columnCollapsed` / `columnCollapsedHeader` / `columnCollapsedTitle` styles with `writingMode: 'vertical-rl'` + `transform: 'rotate(180deg)'`). Set-based state generalises to N panes (future-proof for a possible 4th pane). **Build**: SpaarkeAi 895.04 KB gzip (vs 894.21 task 092 baseline = +0.83 KB / +0.09%); shared lib rebuilds clean. LegalWorkspace 569.35 KB gzip (vs 569.31 task 092 baseline = +40 bytes from new PaneHeader conditional branch code that LegalWorkspace standalone never traverses at runtime — FR-25 preserved behaviorally; bundle delta is unused dead-conditional cost). ADR-021 (Fluent v9 tokens — uses `colorNeutralBackground2`, `colorNeutralBackground2Hover`, `colorStrokeFocus2`, `colorNeutralForeground3`; no hex / rgba), ADR-022 (React 19 functional), ADR-028 (no auth surface affected — localStorage is client-side state only). Deployed to spaarkedev1. | F (Verification — remediation) | ✅ | 010 | — | — (serial) | FULL | 4 |
| 070 | Deploy SpaarkeAi via `Deploy-SpaarkeAi.ps1` | G (Deploy) | ✅ | 060-063, **065** | 071-074 | — | FULL | 1 |
| 071 | UI smoke — Assistant pane (FR-02..FR-09) | G (Smoke) | 🔄 | 070 | 090 | G | STANDARD | 2 |
| 072 | UI smoke — Workspace pane (FR-10..FR-16) | G (Smoke) | 🔄 | 070 | 090 | G | STANDARD | 2 |
| 073 | UI smoke — Context pane (FR-17..FR-22) | G (Smoke) | 🔄 | 070 | 090 | G | STANDARD | 2 |
| 074 | NFR verification (Lighthouse + History overlay timings) | G (Smoke) | 🔄 | 070 | 090 | G | STANDARD | 2 |
| 090 | Project wrap-up (code-review + adr-check + repo-cleanup + lessons-learned) | H (Wrap-up) | 🔲 | 070-074 (+050, 051 if Phase E) | — | — | STANDARD | 3 |

**Total estimated effort**: ~75 hours (excludes Phase E if skipped). With Phase E: ~80 hours.

---

## Parallel Execution Plan (Waves)

Tasks in the same wave can run simultaneously once prerequisites are met. Each task agent invokes `task-execute` independently. **Hard cap: 6 concurrent agents per wave.**

### Wave 0 — Spike (serial, blocking)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 0 | 001 | none | `notes/spikes/*` only | n/a (single task) |

### Wave 1 — Foundations (4-way parallel)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 1 | 010, 011, 012, 013 | 001 ✅ | Separate modules (Spaarke.UI.Components, WorkspaceTabManager, ActionCard lift, telemetry helpers) | ✅ Yes |

### Wave 2 — Independent Phase B/C/D tasks (≤6 parallel)

After Wave 1 completes, dispatch first-tier independent tasks from B/C/D in parallel (up to 6):

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 2a | 020, 021, 023, 024 | 010-013 ✅ | WelcomePanel, ConversationPane, SprkChat (023 reserves SprkChat lock), `useChatFileAttachment` hook (net-new) | ✅ Yes (23 + 24 don't overlap; 25 waits) |
| 2b | 030, 033, 034, 040, 041, 043 | 010-013 ✅ | WorkspacePane (030), LayoutWizard, DailyBriefing (net-new), ContextPaneController (040), GetStartedCardsWidget (net-new), wizard wrappers (net-new) | ✅ Yes (no file overlaps; max 6 concurrent) |
| 2c | 044, 045 | 010-013 ✅ | Analysis Builder intents, AssignWork launcher (both net-new) | ✅ Yes |

> Note: Waves 2a + 2b + 2c may need to be split further if you want strict 6-agent cap respected per wave. Sequence option: dispatch 2a (4 agents), then 2b (6 agents), then 2c (2 agents) on success.

### Wave 3 — Phase B/C/D dependent tasks (serial within phase, parallel across phases)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 3a | 022 | 021 ✅ | ConversationPane.tsx + new HistoryOverlay.tsx | serial w/ 021 |
| 3b | 025 | 023, 024 ✅ | SprkChat.tsx (serial w/ 023) | serial w/ 023 |
| 3c | 031, 032 | 030 ✅ | WorkspacePane.tsx (serial w/ 030) | serial w/ 030 |
| 3d | 035 | 013, 034 ✅ | DailyBriefingSection.tsx (extends 034) | serial w/ 034 |
| 3e | 042 | 040, 041 ✅ | ContextPaneController.tsx + ContextWidgetRegistry.ts | serial w/ 040 |

Across phases (3a + 3b + 3c + 3d + 3e), these can run in parallel since they touch different files (still respecting 6-agent cap).

### Wave 4 — Phase B final + Phase E (conditional)

| Wave | Tasks | Prerequisite | Notes |
|------|-------|--------------|-------|
| 4a | 026 | 001, 024, 025 ✅ | Frontend attachment-payload wiring — proceeds whether Phase E is in or out |
| 4b | 050 (if needed) | 001 (verdict: extension-needed) | Backend payload extension |
| 4c | 051 (if needed) | 050 ✅ | BFF unit tests |

If spike says NO extension needed: mark 050 + 051 as ⏭️ skipped; proceed directly to Wave 5.

### Wave 5 — Verification (4-way parallel, read-only audits)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 5 | 060, 061, 062, 063 | All Phases B/C/D (+E if executed) ✅ | Audit reports only (notes/*) | ✅ Yes |

### Wave 6 — Deploy (serial)

| Wave | Tasks | Prerequisite | Notes |
|------|-------|--------------|-------|
| 6 | 070 | 060-063 ✅ | Production deploy via `Deploy-SpaarkeAi.ps1` |

### Wave 7 — Smoke (4-way parallel, independent tests)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 7 | 071, 072, 073, 074 | 070 ✅ | Smoke reports only (notes/*) | ✅ Yes |

### Wave 8 — Wrap-up (serial, final)

| Wave | Tasks | Prerequisite | Notes |
|------|-------|--------------|-------|
| 8 | 090 | 071-074 ✅ (+ 050, 051 if Phase E) | Quality gates + repo-cleanup + lessons-learned |

---

## How to Execute Parallel Waves

1. Confirm all prerequisites are ✅ in this index.
2. Invoke `task-execute` skill via the Skill tool with **multiple tool calls in ONE message** (one per task in the wave).
3. Each agent runs task-execute for its task with full context loading.
4. After all agents in the wave complete, run build verification (per [`CLAUDE.md` build-verification-between-waves](../../CLAUDE.md)):
   - If any `.cs` modified → `dotnet build src/server/api/Sprk.Bff.Api/`
   - If any `.ts`/`.tsx` modified → `npm run build` in the relevant package
5. Update this index (🔲 → ✅ for each completed task) before proceeding to next wave.
6. If any task fails → mark 🔄, do NOT skip wave verification, decide retry vs report.

**Respect**:
- 6-agent cap per wave (hard limit — API overload guard).
- Sub-agent permission boundary: tasks with `<parallel-safe>false</parallel-safe>` due to `.claude/` writes MUST run in main session sequentially (none here, but rule still applies).

---

## Critical Path

`001 → (010 ∨ 011 ∨ 012 ∨ 013) → (Phase B/C/D longest chain: 030 → 032 → ...) → 060-063 (verify) → 070 (deploy) → 071-074 (smoke) → 090 (wrap-up)`

Longest chain (Phase C): `001 → 010 → 030 → 032 → 060-063 → 070 → 071-074 → 090` ≈ 11 tasks deep. With parallel execution of other phases, total wall-clock is bounded by this path.

---

## High-Risk Items

| Risk ID | Task | Risk | Mitigation |
|---|---|---|---|
| R-1 | 001 → 026, 050, 051 | Spike result determines whether Phase E exists | Phase E tasks are gated; 026 has conditional behavior path |
| R-3 | 012 → 041 | ActionCard lift may surface coupling issues | Decision documented in `notes/drafts/012-actioncard-decision.md`; alternate path is direct import from LegalWorkspace |
| R-4 | 034 | Daily Briefing endpoint may lack per-user caching | `useDailyBriefing` adds frontend TTL cache (~5 min per ADR-014) |
| R-5 | 063 → potentially follow-up | NFR-09 persistence may need `SessionPersistenceService` extension | Verification surfaces gap; fix task opened mid-Phase F if needed |
| R-6 | 024, 061 | PDF.js + mammoth bundle size may exceed budget | Lazy-load both; verify in task 061 |

---

## File Modification Map (for parallel-safety analysis)

| File | Touched by tasks |
|---|---|
| `src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx` | 020 |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | 021, 022 (serial) |
| `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx` (new) | 022 |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | 023, 025, 026 (serial) |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts` (new) | 024 |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` | 030, 031, 032 (serial) |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts` | 011 |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx` (new) | 032 |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceLandingWidget.tsx` (delete) | 031 |
| `src/solutions/WorkspaceLayoutWizard/src/App.tsx` or `steps/TemplateStep.tsx` | 033 |
| `src/solutions/LegalWorkspace/src/sections/dailyBriefing/*` (new) | 034, 035 (serial) |
| `src/solutions/LegalWorkspace/src/sectionRegistry.ts` | 034 |
| `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` | 040, 042 (serial) |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx` (new) | 041 |
| `src/client/shared/Spaarke.AI.Widgets/src/registry/ContextWidgetRegistry.ts` | 042 |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/*` (new wizard wrappers) | 043, 044, 045 |
| `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/*` (new) | 010 |
| `src/client/shared/Spaarke.UI.Components/src/components/ActionCard/*` (new or verified) | 012 |
| `src/solutions/SpaarkeAi/src/telemetry/errorTelemetry.ts` (new) | 013 |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | 050 (conditional) |
| Test files for above | 011, 010, 013, 024, 033, 050 → 051 |

**Conflict-free parallelization rule**: any two tasks listed against the same file MUST be serialized. This index encodes that via `parallel-safe: false` in the relevant POML files.

---

*This index is the single source of truth for task status. Update 🔲 → ✅ on each task completion. The `task-execute` skill updates this file automatically as part of its protocol.*
