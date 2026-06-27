# Daily Briefing — SpaarkeAi Pattern D Migration (R2) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-18
> **Source**: [`design.md`](design.md)
> **Predecessor**: [`projects/spaarke-daily-update-service/`](../spaarke-daily-update-service/) (R1)

---

## Executive Summary

Migrate the Daily Briefing widget to the canonical SpaarkeAi widget framework **Pattern D dual-use** model (Calendar precedent): one shared component lives in a new dedicated package `@spaarke/daily-briefing-components`, reused by BOTH the standalone code page (`sprk_dailyupdate`) and the SpaarkeAi workspace pane. Fix the empty-state regression on the SpaarkeAi side, fix the dead-hyperlink defect in the BFF `/narrate` channel-prompt builder, surface the actual action items hidden by AI-aggregated "Multiple X" bullets, add native MDA-bell deep-links via `appnotification.data.actions[]`, and de-duplicate three files DailyBriefing currently shares verbatim with other solutions (`MicrosoftToDoIcon`, `authInit`, `runtimeConfig`).

The producer layer (7 scheduled notification playbooks + 4 inline event-driven hooks) is verified healthy and out of scope. R2 is consumer-layer + BFF-prompt + targeted de-duplication.

---

## Scope

### In Scope

- **P1** — SpaarkeAi orphan loader fix (wiring seam for `loadNotificationContext` injection into the LegalWorkspace shim or its successor)
- **P2** — Pattern D hoist into new package `@spaarke/daily-briefing-components` (per Calendar's `@spaarke/events-components` and SmartTodo's `@spaarke/smart-todo-components` precedent). Standalone code page becomes thin host shell (≤100 LOC). SpaarkeAi mounts the same shared component.
- **P2 hooks split** — `useNotificationData` decomposed into `useBriefingNotifications` + `useBriefingPreferences` + `useBriefingActions` for Single Responsibility, independent cache lifetimes, and re-render isolation
- **P2a** — Hybrid aggregation UX: AI-aggregated "Multiple X" bullets render narrative + matter link as today, PLUS always-visible per-item sub-list with own per-item entity link + own per-item Add-to-To-Do + own per-item Dismiss
- **P2b** — BFF `/narrate` channel-prompt fix: include `regardingId` per item + server-side validation of returned `primaryEntityId` against supplied IDs (null-out on mismatch rather than render broken link)
- **P3** — `CreateNotificationNodeExecutor` populates `data.actions[]` alongside `customData.actionUrl` so MDA native bell icon shows clickable "Open" buttons
- **DD** — De-duplication of three files where DailyBriefing currently maintains its own copy of a near-identical artifact:
  - `MicrosoftToDoIcon.tsx` × 3 → hoist to `@spaarke/ui-components/src/icons/`
  - `authInit.ts` × 3 → consolidate into `@spaarke/auth` as a `createCodePageAuthInitializer` factory
  - `runtimeConfig.ts` × 3 → consolidate into `@spaarke/auth` (or new tightly-scoped shared module) as a singleton
- Test coverage for hoisted shared lib (unit + integration where practical)
- Updated architecture docs: `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` Daily Briefing section + `SPAARKEAI-COMPONENT-MODEL.md` for the new package

### Out of Scope

- Producer-layer changes (7 notification playbooks, scheduler, executor) beyond P3 — verified healthy
- New notification playbook categories (similar-documents, similar-matters, budget alerts) — R1 R2-future scope
- Retiring the standalone Daily Briefing code page — explicitly retained (owner decision)
- The 10 lower-proximity duplications found in the audit (`xrmProvider`, `ThemeProvider`, 8 SmartTodo components, 3 SmartTodo utils, `useUserPreferences`, `useTodoItems`, `useFeedTodoSync`, `useWorkspaceLayouts`, `DataverseService` divergence, `queryHelpers` divergence) — recommended for a separate `spaarke-shared-lib-hygiene-r1` project. Preserved in Appendix A.
- Real-time SignalR push for instant notification delivery
- Mobile / responsive layout
- Changes to `sprk_userpreference` schema
- Auto-popup behavior (`useDailyDigestAutoPopup` unchanged)
- `sprk_playbooktype` OptionSet cleanup — owner already applied on 2026-06-18

### Affected Areas

| Path | Description |
|---|---|
| `src/client/shared/Spaarke.DailyBriefing.Components/` (NEW) | New package — `@spaarke/daily-briefing-components`. Components, hooks, types, services |
| `src/client/shared/Spaarke.UI.Components/src/icons/MicrosoftToDoIcon.tsx` (NEW) | Hoisted shared icon |
| `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/` (REMOVE) | Misplaced location — content moves to new package |
| `src/client/shared/Spaarke.Auth/src/` (MODIFY) | New `createCodePageAuthInitializer` factory; shared `runtimeConfig` singleton |
| `src/solutions/DailyBriefing/src/` (SHRINK) | Reduce to thin host shell (≤100 LOC); delete local component/hook/service files now in shared package; delete local `MicrosoftToDoIcon`, `authInit`, `runtimeConfig` |
| `src/solutions/LegalWorkspace/src/sections/dailyBriefing/` (MODIFY) | Replace static `dailyBriefingRegistration` with thin shim consuming new shared factory; delete local `MicrosoftToDoIcon`, `authInit`, `runtimeConfig` |
| `src/solutions/SpaarkeAi/src/main.tsx` (MODIFY) | Inject `loadSpaarkeAiNotificationContext` via factory option or module setter; delete local `authInit`, `runtimeConfig` |
| `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` (MODIFY) | `BuildChannelNarrationPrompt` + post-parse validation of `primaryEntityId` |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` (MODIFY) | Populate `data.actions[]` alongside `customData.actionUrl` |
| `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` + `SPAARKEAI-COMPONENT-MODEL.md` (MODIFY) | Reflect new package + Pattern D migration |

---

## Requirements

### Functional Requirements

#### P1 — Wiring seam

1. **FR-01**: The Daily Briefing section registration in the new shared package MUST accept `loadNotificationContext?: () => Promise<NarrateRequest | null>` as a factory option. When omitted, behavior matches the empty-payload contract today (BFF returns empty bullets; UI renders empty state). — Acceptance: factory signature includes the option; standalone code page omits it and still works; SpaarkeAi provides it and bullets populate.
2. **FR-02**: SpaarkeAi `main.tsx` MUST invoke the factory with `loadNotificationContext: loadSpaarkeAiNotificationContext` alongside the existing `setDefaultWorkspaceRenderer` call. — Acceptance: cold-load of SpaarkeAi workspace pane with notifications present renders TL;DR + non-empty channel bullets matching the underlying `appnotification` rows.

#### P2 — Pattern D hoist into `@spaarke/daily-briefing-components`

3. **FR-03**: A new package `@spaarke/daily-briefing-components` MUST be created at `src/client/shared/Spaarke.DailyBriefing.Components/` with `package.json`, `tsconfig.json`, build config matching `@spaarke/events-components` and `@spaarke/smart-todo-components` precedent. — Acceptance: package builds successfully; consumed by both `src/solutions/DailyBriefing/` and the LegalWorkspace section registration shim.
4. **FR-04**: All Daily Briefing UI components MUST be hoisted to the new package: `DailyBriefingApp` (top-level composer), `TldrSection`, `ActivityNotesSection`, `ChannelHeading`, `NarrativeBullet`, `PreferencesDropdown`, `CaughtUpFooter`, `DigestHeader`, `EmptyState`. — Acceptance: components render identically across both hosts.
5. **FR-05**: All Daily Briefing data/action hooks MUST be hoisted as part of FR-04: `useBriefingNotifications`, `useBriefingPreferences`, `useBriefingActions`, `useBriefingNarration`, `useInlineTodoCreate`. — Acceptance: hooks export from `./hooks` subpath of the new package.
6. **FR-06**: The current `useNotificationData` MUST be decomposed into three independent hooks per the split contract:
   - `useBriefingNotifications(webApi)` → `{ channels, totalUnreadCount, loadingState, error, refetch }`
   - `useBriefingPreferences(webApi, userId)` → `{ preferences, updatePreferences, isLoading, error }`
   - `useBriefingActions(webApi)` → `{ markAsRead, markAllAsRead, dismissAll, refresh }`
   Cross-hook coordination (e.g., preferences change → channel refetch) happens at the **consumer layer via effects**, not via internal coupling (Option A — effect-based invalidation; chosen for explicitness, traceability, idiomatic React, and centralization within `DailyBriefingApp`). — Acceptance: each hook can be consumed independently; `DailyBriefingApp` composes all three with an effect that calls `refetch()` when `preferences.disabledChannels` (or any field affecting notification filtering) changes; no shared internal state across the three hooks.
7. **FR-07**: All hoisted components and hooks MUST accept abstracted dependencies as props/parameters: `authenticatedFetch`, `webApi`, `userId`, `tenantId`, optional callbacks (`onRateLimitError`, `onRecordOpen`). NO solution-local imports from `LegalWorkspace`, `DailyBriefing`, or `SpaarkeAi`. — Acceptance: grep of the new package shows zero imports from `src/solutions/*`.
8. **FR-08**: The standalone code page (`src/solutions/DailyBriefing/`) MUST be reduced to a thin host shell. "Thin host shell" is defined by **content, not LOC**: the shell contains only host-binding plumbing (runtime config resolution, auth initialization, theme bootstrap, Xrm resolution, FluentProvider setup) and mounts `<DailyBriefingApp {...hostBindings} />`. The shell MUST NOT contain any Daily Briefing business logic, component definitions, hooks, services, or types — all of those live in the new shared package. All component, hook, and service files in `src/solutions/DailyBriefing/src/` that have been hoisted MUST be deleted. — Acceptance: `grep -r "function\|const.*=.*=>" src/solutions/DailyBriefing/src/*.tsx` reveals only host-binding code; no business logic; no component definitions other than the shell's `App` composer; no hook definitions; no service implementations. Code is efficient and idiomatic, not artificially compressed.
9. **FR-09**: The BFF `/narrate` client (`briefingService.ts` — request build + fetch + response shape) MUST be hoisted as part of the new package. — Acceptance: `briefingService` exported from `./services` subpath; consumed by `useBriefingNarration`.
10. **FR-10**: The new package's exports MUST follow the established Pattern D subpath contract used by `@spaarke/events-components` and `@spaarke/smart-todo-components`: `./components`, `./widgets`, `./hooks`, `./services`, `./types`. — Acceptance: `package.json` exports map present; consumer imports resolve.

#### P2a — Hybrid aggregation UX

11. **FR-11**: `NarrativeBullet` MUST render a per-item sub-list when `itemIds.length > 1`. Sub-list rendered as compact indented rows beneath the narrative line. Each sub-row contains: per-item entity link (display name + entity icon hint) + Add-to-To-Do button + Dismiss button. — Acceptance: aggregated bullet with N>1 underlying items renders N sub-rows; single-item bullets (N=1) render unchanged (no sub-list).
12. **FR-12**: Sub-row entity link MUST use the supplied `regardingEntityType` + `regardingId` from the underlying `NotificationItem` (no AI involvement). On click, opens the entity record in a Dataverse modal dialog via `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName, entityId }, { target: 2, width: 80%, height: 80% })`. — Acceptance: every sub-row link opens the correct record. No broken sub-row links.
13. **FR-13**: Sub-row Add-to-To-Do button MUST invoke `useInlineTodoCreate` with the specific underlying `NotificationItem` (real title, body, regarding lookup). Creates a concrete `sprk_todo` row, not a vague aggregated summary. — Acceptance: clicking a sub-row To Do creates a `sprk_todo` whose `sprk_name` matches the underlying notification title; regarding lookup resolves to the underlying notification's `regardingId`.
14. **FR-14**: Sub-row Dismiss button MUST mark only the specific underlying `appnotification` row as read (not the entire aggregate). The sub-row fades / hides on success. — Acceptance: dismissing one sub-row leaves the others present; only the targeted notification's `isread` flips to true.

14a. **FR-14a**: Aggregated-bullet Dismiss button (when the user dismisses the aggregated bullet itself, not a sub-row) MUST cascade — mark ALL underlying `appnotification` rows in `itemIds[]` as read. The entire bullet (narrative + sub-list) fades / hides on success. — Acceptance: dismissing an aggregated bullet flips `isread = true` on every notification in `itemIds[]`; the bullet and all its sub-rows disappear from the briefing.

#### P2b — BFF `/narrate` fix

15. **FR-15**: `BuildChannelNarrationPrompt` MUST emit each item line including `regardingId` so the LLM has a real ID to return as `primaryEntityId`. Format: `- [id={item.Id} regardingId={item.RegardingId}] Title | ... | regarding: Name (Type) | ...`. — Acceptance: prompt builder output (logged or unit-tested) contains `regardingId=` for every item that has one.
16. **FR-16**: `BuildChannelNarrationPrompt` rule list MUST instruct the LLM: "Set `primaryEntityType` to the regarding entity type of the most relevant supplied item. Set `primaryEntityId` to the matching `regardingId` of the most relevant supplied item. Do NOT use the `[id=...]` notification ID. Do NOT invent IDs." — Acceptance: prompt text contains these instructions.
17. **FR-17**: After `ParseChannelBullets`, the server MUST validate each bullet's `primaryEntityId` against the union of supplied `item.RegardingId` values for the channel. If no match: null out `primaryEntityType` + `primaryEntityId` + `primaryEntityName` on that bullet (so the frontend renders no link) and log a warning with the offending bullet for monitoring. — Acceptance: unit test with a mocked LLM returning a hallucinated `primaryEntityId` produces a response with nulled primaryEntity fields; warning logged.

#### P3 — Native MDA bell deep-links

18. **FR-18**: `CreateNotificationNodeExecutor` MUST populate `data.actions` as a single-entry array `[{ title: "Open", data: { url: <actionUrl> } }]` when (a) an `actionUrl` is present AND (b) the notification's `toasttype` indicates a visible toast (i.e., NOT "Hidden"). Hidden-toast notifications skip `data.actions[]` population (no visible bell surface to render the action). Both `iterateItems` per-item creation and the standard single-notification creation paths covered. The existing `customData.actionUrl` is populated regardless of `toasttype` (consumed by Daily Briefing UI, not MDA bell). — Acceptance: after a scheduler tick, a freshly-created `appnotification` row with a visible `toasttype` has `data.actions[0].data.url` equal to `customData.actionUrl`; a row with `toasttype = Hidden` has `data.actions` null or absent; MDA native bell icon renders clickable "Open" buttons for the visible ones.

#### DD — De-duplication (high-proximity files)

19. **FR-19**: `MicrosoftToDoIcon.tsx` MUST be hoisted to `@spaarke/ui-components/src/icons/MicrosoftToDoIcon.tsx` and exported from the `@spaarke/ui-components` icons surface. The three solution-local copies (`src/solutions/LegalWorkspace/src/icons/MicrosoftToDoIcon.tsx`, `src/solutions/SmartTodo/src/icons/MicrosoftToDoIcon.tsx`, `src/solutions/DailyBriefing/src/icons/MicrosoftToDoIcon.tsx`) MUST be deleted, with all consumers updated to import from `@spaarke/ui-components`. — Acceptance: `find src/solutions -name "MicrosoftToDoIcon.tsx"` returns zero results; build green; visual diff null.
20. **FR-20**: `authInit.ts` MUST be consolidated into `@spaarke/auth` as `createCodePageAuthInitializer(config)` factory returning the `{ ensureAuthInitialized, authenticatedFetch, getTenantId }` triple. The three solution-local copies (`DailyBriefing`, `LegalWorkspace`, `SpaarkeAi`) MUST be deleted, with `main.tsx` in each solution calling the factory. — Acceptance: zero `authInit.ts` files under `src/solutions/`; each solution's `main.tsx` calls `createCodePageAuthInitializer`; build green; auth flow unchanged.
21. **FR-21**: `runtimeConfig.ts` MUST be consolidated into `@spaarke/auth` (or a new tightly-scoped `@spaarke/runtime-config` module) as a shared singleton. The three solution-local copies MUST be deleted. — Acceptance: zero `runtimeConfig.ts` files under `src/solutions/`; each solution imports `resolveRuntimeConfig` from the shared module; runtime config resolution unchanged.

### Non-Functional Requirements

- **NFR-01**: BFF `/narrate` end-to-end latency ≤ 3 seconds (existing target preserved). Per-channel narration ≤ 2 seconds. Validate via App Insights monitoring after deploy.
- **NFR-02**: Standalone Daily Briefing code page visual rendering — minor shifts (spacing, token resolution, sub-pixel) are acceptable if they fall out of the hoist; document any non-trivial deviation in the implementation PR. FR-25 / NFR-10 byte-stability from R1 is **relaxed** per owner decision (2026-06-18).
- **NFR-03**: Dark mode renders correctly across all hoisted components via Fluent v9 semantic tokens (ADR-021). No hard-coded colors.
- **NFR-04**: BFF publish-size delta ≤ +1 MB compressed vs. baseline (~45.65 MB per 2026-05-26 §10 NFR-01 measurement). The only BFF changes (P2b + P3) are pure code; no new NuGet dependencies expected. Verify via `dotnet publish -c Release -o deploy/api-publish/` and report absolute size + diff in PR per BFF hygiene rule §10.
- **NFR-05**: Test coverage — new shared package `@spaarke/daily-briefing-components` ships with unit tests for the 3 split hooks + a smoke test that mounts `DailyBriefingApp` with mocked Xrm and asserts the BFF `/narrate` call fires with a non-empty payload (filling the test gap identified in the assessment).
- **NFR-06**: No new HIGH-severity CVEs from `dotnet list package --vulnerable --include-transitive` after BFF changes (per CLAUDE.md §10 bullet 5).
- **NFR-07**: All hoist + de-duplication PRs MUST pass `code-review` skill + `adr-check` skill at Step 9.5 of the FULL rigor `task-execute` protocol.

---

## Technical Constraints

### Applicable ADRs

- **ADR-001**: BFF Minimal API — `/narrate` prompt fix (P2b) and `CreateNotificationNodeExecutor` change (P3) stay inside `Sprk.Bff.Api`; no Azure Functions, no separate services.
- **ADR-006**: Code Page pattern — standalone Daily Briefing remains a Vite + React 19 + Fluent v9 Code Page; not converted to PCF.
- **ADR-010**: DI minimalism — no new BFF DI registrations needed for R2.
- **ADR-012**: Shared components — the canonical "when to add to shared library" criteria are met for every DD item AND for P2's hoist (used by 2+ surfaces; core Spaarke UX pattern). Pattern D dual-use is the canonical shape.
- **ADR-013**: AI features extend BFF — `/narrate` endpoint change (P2b) stays in BFF; no new AI endpoint.
- **ADR-021**: Fluent UI v9 exclusively, semantic tokens, dark mode required throughout.
- **ADR-024**: Multi-entity regarding resolution — `useInlineTodoCreate` already implements this via `TODO_REGARDING_CATALOG`; preserve in the hoist.
- **ADR-028**: Auth contract — `@spaarke/auth` provides true SSO; the new `createCodePageAuthInitializer` factory (FR-20) is the canonical consumption pattern.

### MUST Rules

- ✅ MUST create new package `@spaarke/daily-briefing-components` per Calendar + SmartTodo precedent (NOT extend `@spaarke/ui-components`)
- ✅ MUST split `useNotificationData` into 3 independent hooks per the split contract (FR-06)
- ✅ MUST validate AI-returned `primaryEntityId` server-side against supplied `regardingId` set; null-out invalid responses rather than render broken links (FR-17)
- ✅ MUST use the supplied `regardingId` (not AI output) for per-item sub-row hyperlinks in P2a (FR-12)
- ✅ MUST preserve the standalone Daily Briefing code page as a working surface
- ✅ MUST follow the Pattern D dual-use pattern documented in [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md)
- ✅ MUST update `SPAARKEAI-COMPONENT-MODEL.md` and `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` to reflect the new package
- ❌ MUST NOT resurrect `WorkspaceHomeTab.tsx` (deleted in PR #375; the post-task-109 unified pipeline is canonical)
- ❌ MUST NOT introduce new BFF endpoints (use existing `/narrate`)
- ❌ MUST NOT retire the standalone Daily Briefing code page in R2
- ❌ MUST NOT bundle the lower-proximity duplications from Appendix A — those belong to a separate project

### Existing Patterns

- **Pattern D dual-use precedent**: `@spaarke/events-components` ([`src/client/shared/Spaarke.Events.Components/`](../../src/client/shared/Spaarke.Events.Components/)) for Calendar; `@spaarke/smart-todo-components` ([`src/client/shared/Spaarke.SmartTodo.Components/`](../../src/client/shared/Spaarke.SmartTodo.Components/)) for Smart To Do. The new `@spaarke/daily-briefing-components` mirrors their package layout, build config, and exports surface.
- **LegalWorkspace section shim precedent**: the 62-line Calendar registration shim at [`src/solutions/LegalWorkspace/src/sections/calendar.registration.ts`](../../src/solutions/LegalWorkspace/src/sections/calendar.registration.ts) is the model for the post-hoist Daily Briefing registration.
- **Thin host shell precedent**: [`src/solutions/EventsPage/`](../../src/solutions/EventsPage/) standalone code page demonstrates the post-hoist shape (auth + theme + Xrm bootstrap → mount shared component).
- **Auth contract**: per [`docs/architecture/spaarke-sso-binding.md`](../../docs/architecture/spaarke-sso-binding.md) (referenced via CLAUDE.md), `@spaarke/auth` is the canonical entry point; the new `createCodePageAuthInitializer` factory (FR-20) is the standardization.
- **Multi-entity regarding resolution**: `TODO_REGARDING_CATALOG` + `applyResolverFields` already implemented in `useInlineTodoCreate.ts:160-261`; preserve verbatim during hoist.

---

## Success Criteria

1. [ ] **SpaarkeAi workspace pane renders Daily Briefing with real notifications** — for a user with N>0 unread `appnotification` records, the workspace pane's "Daily Briefing" tab shows TL;DR + channel sections + bullets matching the standalone code page for the same user. Verify: open SpaarkeAi as `ralph.schroeder@spaarke.com` in spaarkedev1.
2. [ ] **Same component renders in both hosts** — one component file (`DailyBriefingApp.tsx`) in `@spaarke/daily-briefing-components` is mounted by both the standalone code page and the SpaarkeAi widget. Verify: grep both hosts for `DailyBriefingApp` imports; both resolve to the new package.
3. [ ] **No dead hyperlinks** — for every rendered bullet AND every per-item sub-row, clicking the entity link opens a modal dialog with the correct record. Verify: click 10 random links across categories; 10/10 open the right record.
4. [ ] **Aggregated bullets surface action items** — every "Multiple X" bullet (where `itemIds.length > 1`) has a visible per-item sub-list below it. Each sub-row has own entity link + own To Do + own Dismiss. Verify: visual inspection on a briefing with at least one aggregated bullet.
5. [ ] **Add-to-To-Do creates concrete `sprk_todo` records** — clicking a sub-row To Do produces a `sprk_todo` whose `sprk_name`/`sprk_notes`/regarding match the underlying notification, not the aggregated summary. Verify: create one from an aggregated bullet; query Dataverse; confirm field-level fidelity.
6. [ ] **BFF `/narrate` prompt includes `regardingId`** + server-side `primaryEntityId` validation works. Verify: unit test asserts prompt content; unit test asserts hallucinated `primaryEntityId` → nulled response fields.
7. [ ] **MDA native bell icon shows clickable "Open" buttons** for newly-created notifications. Verify: trigger one notification (e.g., upload a document); open MDA bell; confirm "Open" action visible and functional.
8. [ ] **Standalone Daily Briefing code page behavior unchanged for end users** (per relaxed NFR-02). Verify: visual comparison of pre/post hoist; document any deviation in PR.
9. [ ] **DD: zero `MicrosoftToDoIcon.tsx` files under `src/solutions/`** — `find src/solutions -name "MicrosoftToDoIcon.tsx"` returns zero. Same for `authInit.ts` and `runtimeConfig.ts`. Verify via grep.
10. [ ] **`useNotificationData` split** — three hooks (`useBriefingNotifications`, `useBriefingPreferences`, `useBriefingActions`) export from the new package; old monolithic hook deleted. Verify: grep for `useNotificationData` returns zero hits in the new package.
11. [ ] **`@spaarke/daily-briefing-components` package builds and publishes locally** — `npm run build` in the new package succeeds; consumers can `import` from `./components`, `./hooks`, `./services`, `./types` subpaths.
12. [ ] **Dark mode unaffected** — all hoisted components use Fluent v9 semantic tokens; manual toggle test passes.
13. [ ] **BFF publish-size delta ≤ +1 MB compressed** — measured per BFF hygiene rule §10 NFR-01 verification procedure. Report absolute + diff in PR.
14. [ ] **Architecture docs updated** — `SPAARKEAI-COMPONENT-MODEL.md` lists `@spaarke/daily-briefing-components`; `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` Daily Briefing section references the new package; `BUILD-A-NEW-WORKSPACE-WIDGET.md` table of dual-use widgets updated.

---

## Dependencies

### Prerequisites

- Producer layer operational (verified 2026-06-18 via Dataverse query)
- BFF `/narrate` endpoint operational (verified)
- `@spaarke/ui-components` shared lib build pipeline operational
- `@spaarke/auth` exporting `useAuth` / `initAuth` / `authenticatedFetch` (in use)
- spaarkedev1 access for E2E verification

### External

- Azure OpenAI service available for `/narrate` (existing dependency)
- Microsoft Graph / Dataverse Web API for `Xrm.WebApi` interactions (existing dependency)

### Related Projects

- [`projects/spaarke-daily-update-service/`](../spaarke-daily-update-service/) — R1 predecessor; producer layer + standalone code page + Phase 8 narrative redesign
- Recommended follow-on: **`spaarke-shared-lib-hygiene-r1`** — separate project to address the 10 lower-proximity duplications surfaced in Appendix A
- `spaarke-ai-workspace-UI-r1` followup-backlog — Item 1 (Modal-preview record-open standard) overlaps with R2's per-item modal UX; coordinate
- Calendar widget hoist (`@spaarke/events-components`, R3 task 115) — Pattern D precedent; review during P2

---

## Owner Clarifications

Answers captured during the design-to-spec interview (2026-06-18 architecture review session):

| Topic | Question | Answer | Impact |
|---|---|---|---|
| `appnotification` core vs. value-add | Is `appnotification` a CORE component or value-add? | **CORE.** Design intentionally uses native entity for MDA bell + persistent read state + executor idempotency + multi-consumer producer layer. | Producer layer architecture is correct as-is; P3 native bell deep-links worth adding to maximize the native UX surface. |
| Standalone code page retention | Should the standalone Daily Briefing code page be retired in this project? | **NO** — maintain it. It may be used as a standalone surface. | P2 hoist must preserve the standalone code page as a thin host shell, not retire it. Pattern D dual-use shape required. |
| "Multiple X" aggregation UX | How to surface the underlying items hidden by AI aggregation? | **Option A — Hybrid**: narrative bullet + always-visible per-item sub-list with own per-item links + actions. | Drives FR-11 through FR-14. Per-item sub-row links use supplied `regardingId` directly (no AI involvement) → fixes both the dead-link AND the vague-To-Do problems. |
| Hoist package location | New dedicated package, or extend `@spaarke/ui-components`? | **New package `@spaarke/daily-briefing-components`** per Calendar + SmartTodo precedent. Spaarke convention (codified in [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) Pattern D §1.3) is one package per dual-use widget. | Drives FR-03 + FR-10 (package layout matches established precedent). |
| `useNotificationData` split | Keep coupled or split? | **Split** — correct technical approach per Single Responsibility, independent cache lifetimes, re-render isolation, composability. Effort cost not a factor. | Drives FR-06 (three-hook decomposition). |
| Byte-stability of standalone | Strict FR-25/NFR-10 parity, or relaxed? | **Relaxed** — minor shifts OK if documented. | Drives NFR-02. Avoids blocking shared-component improvements on standalone parity. |
| `MicrosoftToDoIcon` location | Where do shared icons live? | **`@spaarke/ui-components/src/icons/`** (existing `SprkIcons` registry location). Found 3 copies of `MicrosoftToDoIcon` — hoist canonical to shared lib, delete copies. | Drives FR-19 (DD). |
| DD scope boundary | Bundle all 14 duplication findings into R2, or scope? | Bundle the 4 high-proximity (DailyBriefing already touches these files). Defer the 10 lower-proximity to a separate `spaarke-shared-lib-hygiene-r1` project. **Do not let the icon fix get buried as tech debt** — addressed in DD workstream. | Drives FR-19 through FR-21 (3 high-proximity DD items in scope); Appendix A preserves the 10 deferred items. |
| `sprk_playbooktype` OptionSet | Cleanup needed? | **Resolved out-of-band by owner 2026-06-18.** Final state: `AiAnalysis(0) / Workflow(1) / Notification(2) / Hybrid(3)`. | Out of scope for R2 (already shipped). |

---

## Assumptions

Proceeding with these assumptions (owner did not specify or confirmation pending):

- **Package naming**: assuming `@spaarke/daily-briefing-components` (full name for clarity). Owner can adjust during implementation (e.g., `@spaarke/briefing-components`). Decision deferred to first PR.
- **`createCodePageAuthInitializer` location**: assuming the factory lives in `@spaarke/auth` rather than a new package. If team prefers a dedicated `@spaarke/runtime-config` module, surface during implementation review.
- **MDA bell `data.actions[]` format**: assuming Dataverse native schema `{ actions: [{ title, data: { url } }] }`. If platform docs disagree, surface during P3 implementation.
- **Sub-row visual density**: assuming Fluent v9 `fontSizeBase200` + `spacingVerticalS` for the per-item sub-list. May tune during implementation; final shape decided in PR review.
- **Cross-hook coordination after split**: assuming consumers (standalone code page) implement effect-based coordination (preferences change → notifications refetch) at the App.tsx level. No shared internal state in the new hooks.
- **MicrosoftToDoIcon SVG**: assuming the 3 solution-local copies are byte-identical (per audit "byte-for-byte identical except imports"). If divergence exists, surface during DD implementation and reconcile.

---

## Unresolved Questions

All blocking questions resolved during spec-review (2026-06-18). Captured below for traceability:

- [x] ~~Cross-hook cache invalidation strategy~~ — **Resolved**: Option A (effect-based at consumer layer). `DailyBriefingApp` composes the 3 split hooks and runs an effect that calls `refetch()` on the notifications hook when `preferences.disabledChannels` (or any filtering-relevant field) changes. Idiomatic React, explicit, traceable, no hidden coupling. See FR-06.
- [x] ~~Per-item sub-row Add-to-To-Do behavior when subset already converted~~ — **Resolved**: Dismissing the aggregated bullet cascades — all underlying notifications in `itemIds[]` are marked read regardless of prior per-item To Do conversion state. See FR-14a.
- [x] ~~MDA bell render verification across `toasttype` values~~ — **Resolved**: `data.actions[]` is populated only when `toasttype` indicates a visible toast. Hidden-toast notifications skip `data.actions[]` (no visible bell surface to render the action; population is unnecessary). See FR-18.
- [x] ~~Package boundary for `briefingService` BFF client~~ — **Resolved**: The BFF client (`briefingService`) lives in the new dual-use package per Calendar precedent (`@spaarke/events-components` keeps its BFF client package-local). No generic `@spaarke/bff-clients` package today; create one only when a real second consumer demands it. See FR-09.

*No outstanding blockers. Ready for implementation.*

---

## Related Proposal

The broader duplication audit conducted 2026-06-18 surfaced 10 lower-proximity findings beyond the 3 in R2's DD workstream. These are captured in a separate proposal document for evaluation as a follow-on project:

📄 **[`shared-lib-hygiene-proposal.md`](shared-lib-hygiene-proposal.md)** — Proposal for `spaarke-shared-lib-hygiene-r1`. Catalogs the 10 deferred findings with fix-size estimates and recommended targets. To be evaluated separately.

---

*AI-optimized specification. Original design: [`design.md`](design.md). Generated 2026-06-18 from architecture review session with full Dataverse-environment verification.*
