# Project Plan: Spaarke AI Platform Unification R3

> **Last Updated**: 2026-05-20
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Reshape the SpaarkeAi welcome state per "Moment 1: Arrival" — unified `<PaneHeader>` primitive across all three panes, Assistant chrome trim + multi-file attach + History overlay, Workspace pane embeds LegalWorkspace as Home tab with Daily Briefing surface, Context pane swaps PlaybookGallery for 7 GetStarted action cards routing wizards.

**Scope**:
- New shared `<PaneHeader>` primitive in `@spaarke/ui-components` (foundation)
- Assistant pane refactor (FR-02..FR-09): 7 functional requirements
- Workspace pane refactor (FR-10..FR-16): 7 functional requirements
- Context pane refactor (FR-17..FR-22): 6 functional requirements
- Cross-cutting: error-only telemetry, auth-via-`authenticatedFetch`, backwards-compatibility (FR-23..FR-25): 3 functional requirements
- Possible BFF endpoint extension for `attachments[]` (FR-07 backend, conditional on spike result)

**Timeline**: 4–6 weeks | **Estimated Effort**: 60–80 hours (30–40 tasks @ 2–3 h each + spikes + deploy/verification)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006**: PCF over web resources — confirms SpaarkeAi + LegalWorkspace are full-page custom pages, not PCFs (architectural placement guard).
- **ADR-008**: Endpoint filters — any new/changed backend endpoint inherits existing auth/rate-limit/validation pipeline.
- **ADR-010**: DI minimalism — register any new BFF service in an existing feature module; no new modules.
- **ADR-012 (LOAD-BEARING)**: Shared components live in `@spaarke/ui-components`; `<PaneHeader>` and (if needed) `ActionCard` lifted here. All context-agnostic components → shared lib, not solution-local.
- **ADR-013**: AI architecture — Daily Briefing and chat-attachment paths extend the BFF in-process; no new service.
- **ADR-014**: AI caching — Daily Briefing response cached per-user with short TTL (~5 min) to avoid hammering LLM on tab switches.
- **ADR-016**: AI rate limits — Daily Briefing endpoint subject to existing rate-limit filters; widget shows graceful degraded UI on 429.
- **ADR-021 (LOAD-BEARING)**: Fluent v9 tokens only. No hardcoded `#` colors, no `rgba(...)` literals, no Fluent v8. Dark-mode safe by construction.
- **ADR-022**: React 19 + platform-library boundaries — React 19 for all SpaarkeAi/LegalWorkspace code; affects bundling for PDF.js.
- **ADR-025**: Icon library — Fluent v9 `@fluentui/react-icons` only (`ChatRegular`, `AppsListRegular`, `DocumentRegular`, `HistoryRegular`, `AttachRegular`); no new SVG web resources.
- **ADR-026**: Full-page custom pages — Vite + React 19 bundled; all new shared components built per this standard.
- **ADR-028 (LOAD-BEARING)**: Spaarke auth v2 — all BFF calls go through `authenticatedFetch` from `@spaarke/auth`. Function-based contract per INV-1..INV-8. No token snapshots in props or state. Per-request `getAccessToken()`.

**From Spec**:
- **MUST NOT** create Dataverse Document entities from the Assistant `+` button (in-memory message context only).
- **MUST NOT** introduce React 16 fallbacks; React 19 only.
- **MUST NOT** add happy-path App Insights events in this project; error-only telemetry per OC-09.
- **MUST** keep the standalone LegalWorkspace code page unchanged (NFR-10, FR-25).

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Embed LegalWorkspace via `WorkspaceShell` (not rebuild) | Reuse shipping code; one source of truth for layouts/sections/persistence | Workspace pane code is thin composition + `WorkspacePaneMenu`; LegalWorkspace untouched |
| Side overlay for History (Claude-Code style) | OC-01; replaces conversation in place | `HistoryOverlay.tsx` is a new component; no tab system to maintain in ConversationPane |
| Multi-file (≤5) extracted client-side | OC-02 / OC-07; avoids new SPE pipeline | New `useChatFileAttachment` hook + lazy PDF.js/mammoth; possibly small BFF payload extension |
| `MAX_WORKSPACE_TABS = 8` configurable, Home exempt | OC-03; predictable FIFO eviction | Constant in one place in [`WorkspaceTabManager.ts`](../../src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts) |
| Error-only telemetry | OC-09; scope discipline | Telemetry helper module + 3 emission sites (Daily Briefing 429, file extraction failure, HistoryOverlay load failure) |
| Stay on existing `work/spaarke-ai-platform-unification-r3` worktree branch | Repo worktree convention | Step 4 of /project-pipeline does not create new branch; commits land on this worktree |

### Discovered Resources

**Applicable ADRs** (all confirmed present in concise + full form):

| ADR | Concise | Full | Load-Bearing |
|-----|---------|------|--------------|
| ADR-006 | [.claude/adr/ADR-006-*.md](../../.claude/adr/) | [docs/adr/ADR-006-*.md](../../docs/adr/) | |
| ADR-008 | [.claude/adr/ADR-008-*.md](../../.claude/adr/) | [docs/adr/ADR-008-*.md](../../docs/adr/) | |
| ADR-010 | [.claude/adr/ADR-010-*.md](../../.claude/adr/) | [docs/adr/ADR-010-*.md](../../docs/adr/) | |
| **ADR-012** | [.claude/adr/ADR-012-*.md](../../.claude/adr/) | [docs/adr/ADR-012-*.md](../../docs/adr/) | **YES** |
| ADR-013 | [.claude/adr/ADR-013-*.md](../../.claude/adr/) | [docs/adr/ADR-013-*.md](../../docs/adr/) | |
| ADR-014 | [.claude/adr/ADR-014-*.md](../../.claude/adr/) | [docs/adr/ADR-014-*.md](../../docs/adr/) | |
| ADR-016 | [.claude/adr/ADR-016-*.md](../../.claude/adr/) | [docs/adr/ADR-016-*.md](../../docs/adr/) | |
| **ADR-021** | [.claude/adr/ADR-021-*.md](../../.claude/adr/) | [docs/adr/ADR-021-*.md](../../docs/adr/) | **YES** |
| ADR-022 | [.claude/adr/ADR-022-*.md](../../.claude/adr/) | [docs/adr/ADR-022-*.md](../../docs/adr/) | |
| ADR-025 | [.claude/adr/ADR-025-*.md](../../.claude/adr/) | [docs/adr/ADR-025-*.md](../../docs/adr/) | |
| ADR-026 | [.claude/adr/ADR-026-*.md](../../.claude/adr/) | [docs/adr/ADR-026-*.md](../../docs/adr/) | |
| **ADR-028** | [.claude/adr/ADR-028-*.md](../../.claude/adr/) | [docs/adr/ADR-028-*.md](../../docs/adr/) | **YES** |

**Applicable Skills** (referenced in task `<skills>` sections):

- [`.claude/skills/task-execute/`](../../.claude/skills/task-execute/) — mandatory protocol for every task
- [`.claude/skills/adr-aware/`](../../.claude/skills/adr-aware/) — auto-loads ADRs when creating resources
- [`.claude/skills/script-aware/`](../../.claude/skills/script-aware/) — discovers reusable scripts before writing new code
- [`.claude/skills/code-page-deploy/`](../../.claude/skills/code-page-deploy/) — build + deploy React 19 Code Pages (Vite + singlefile)
- [`.claude/skills/ui-test/`](../../.claude/skills/ui-test/) — browser-based smoke testing for Code Pages
- [`.claude/skills/code-review/`](../../.claude/skills/code-review/) — quality gate after implementation
- [`.claude/skills/adr-check/`](../../.claude/skills/adr-check/) — validate code against loaded ADRs
- [`.claude/skills/dataverse-deploy/`](../../.claude/skills/dataverse-deploy/) — deploy web resources + solutions for AssignWork custom page reference
- [`.claude/skills/push-to-github/`](../../.claude/skills/push-to-github/) — branch commit + push
- [`.claude/skills/merge-to-master/`](../../.claude/skills/merge-to-master/) — final merge with safety checks
- [`.claude/skills/repo-cleanup/`](../../.claude/skills/repo-cleanup/) — project end hygiene

**NOT applicable**: `pcf-deploy` (SpaarkeAi is a full-page custom page, not a PCF).

**Applicable Patterns**:

- [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) — canonical MSAL invariants (INV-1..INV-8) + v2 token contract
- [`.claude/patterns/auth/bff-url-normalization.md`](../../.claude/patterns/auth/bff-url-normalization.md) — `buildBffApiUrl()` for API calls
- [`.claude/patterns/webresource/full-page-custom-page.md`](../../.claude/patterns/webresource/full-page-custom-page.md) — Vite entry setup, React 19 pattern, Fluent provider, URL params
- [`.claude/patterns/webresource/code-page-wizard-wrapper.md`](../../.claude/patterns/webresource/code-page-wizard-wrapper.md) — thin wrapper pattern around shared dialogs

**Applicable Guides**:

- [`docs/guides/SHARED-UI-COMPONENTS-GUIDE.md`](../../docs/guides/SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/ui-components` build, consumption, deep imports
- [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) — Auth v2 operator runbook (Dataverse env vars, App Service settings, MI, smoke tests)
- [`docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md`](../../docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md) — post-deploy smoke tests for Code Pages + BFF
- [`docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md`](../../docs/guides/WORKSPACE-AI-PREFILL-GUIDE.md) — workspace launch-point context & initialization
- [`docs/guides/WORKSPACE-ENTITY-CREATION-GUIDE.md`](../../docs/guides/WORKSPACE-ENTITY-CREATION-GUIDE.md) — entity creation from workspaces (Matter, Project)

**Build/Deploy Scripts**:

- [`scripts/Build-ViteSolutionsDirect.ps1`](../../scripts/Build-ViteSolutionsDirect.ps1) — primary build entry for Vite Code Page solutions
- [`scripts/Deploy-SpaarkeAi.ps1`](../../scripts/Deploy-SpaarkeAi.ps1) — deploy SpaarkeAi three-pane shell + workspace embed
- [`scripts/Deploy-LegalWorkspaceCustomPage.ps1`](../../scripts/Deploy-LegalWorkspaceCustomPage.ps1) — standalone LegalWorkspace (regression target)
- [`scripts/Test-Deployment.ps1`](../../scripts/Test-Deployment.ps1) — post-deploy smoke verification

**Reusable Code (DO NOT reinvent)**:

- `<PaneHeader>` style reference: [`src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx:142-171, 691-700`](../../src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx) — canonical visual model
- `PaneEventBus` channels: [`src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx`](../../src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx) — `workspace`, `context`, `conversation`, `safety`
- `authenticatedFetch` + `useAuth()`: `@spaarke/auth` per ADR-028 INV-1..INV-8
- `WorkspaceShell`, `useWorkspaceLayouts`: [`src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts`](../../src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts) — embed as-is
- `ActionCard` + handlers split: [`src/solutions/LegalWorkspace/src/components/GetStarted/`](../../src/solutions/LegalWorkspace/src/components/GetStarted/) — reuse, possibly lift to shared lib (A-3)
- `CreateProjectWizardDialog`, `FindSimilarWizardDialog`: existing dialogs — wrap into widget wrappers (FR-19)
- `CreateMatterWizardWidget`, `DocumentUploadWizardWidget`: already in `WorkspaceWidgetRegistry`
- `sectionRegistry` + `*.registration.ts`: [`src/solutions/LegalWorkspace/src/sectionRegistry.ts`](../../src/solutions/LegalWorkspace/src/sectionRegistry.ts) — Daily Briefing follows this pattern

**R2 lessons to inherit** (from [`projects/spaarke-ai-platform-unification-r2/notes/lessons-learned.md`](../spaarke-ai-platform-unification-r2/notes/lessons-learned.md)):
- PaneEventBus multi-subscriber pattern (ADR candidate, still pending)
- Widget registry pattern (Workspace + Context separate, shared interface)
- Data-refreshed restore (D-08) — re-fetch fresh data on restore vs replaying snapshots
- Three-pane lifecycle stages (Welcome/Active/Review/Complete)
- Single-LLM-call invariant (D-01)
- Write-through Cosmos persistence (D-06)

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Spike (1 task, serial, blocking)
└─ FR-07 backend payload verification (30 min)

Phase A: Foundations (4 tasks, parallel-safe)
└─ <PaneHeader>, MAX_WORKSPACE_TABS, ActionCard lift, telemetry helpers

Phase B: Assistant pane (7 tasks, parallel within phase after A)
└─ Welcome chrome trim, PaneHeader migration, HistoryOverlay, input-on-load,
   file-attach hook + toolbar restructure + attachment payload wiring

Phase C: Workspace pane (6 tasks, parallel within phase after A)
└─ PaneHeader + LegalWorkspace embed, delete LandingWidget, WorkspacePaneMenu,
   layout templateFilter, Daily Briefing section + 429/empty state

Phase D: Context pane (6 tasks, parallel within phase after A)
└─ PaneHeader + stage label, GetStartedCardsWidget, wizard wrappers,
   AB intents, AssignWorkLauncher

Phase E: Backend (conditional, 2 tasks, only if Phase 0 spike requires)
└─ ChatEndpoints attachments[] schema + tests

Phase F: Integration & verification (4 tasks, serial after B+C+D)
└─ Auth audit, bundle-size, dark-mode token audit, backwards-compat

Phase G: Deploy + smoke (5 tasks, serial after F)
└─ Deploy SpaarkeAi, per-pane UI smoke, NFR verification

Phase H: Wrap-up (1 task, serial)
└─ Lessons-learned, README → Complete, archive
```

### Critical Path

**Blocking dependencies:**
- Phase A blocks Phases B, C, D (all consume `<PaneHeader>`, telemetry helpers, ActionCard)
- Phase 0 blocks task 026 in Phase B (attachment payload wiring) and Phase E (if needed)
- Phase F blocks Phase G (deploy only after verification passes)
- Phase G blocks Phase H

**High-risk items:**
- **Backend payload spike (task 001)**: outcome determines whether Phase E exists. Mitigation: 30-min timeboxed spike.
- **PDF.js + mammoth bundle size (NFR-12)**: lazy-loaded; verified via bundle analyzer in task 061.
- **Session persistence of non-Home tabs (NFR-09 / UQ-03)**: may require BFF/Cosmos extension. Mitigation: verify in Phase F task 063; opens sub-task if gap surfaces.

---

## 4. Phase Breakdown

### Phase 0: Spike — FR-07 Backend Payload Verification

**Objectives:**
1. Determine whether `POST /api/ai/chat/sessions/{sessionId}/messages` accepts an `attachments[]` field today, or requires schema extension.

**Deliverables:**
- [ ] Spike report (15–30 min) in `notes/spikes/001-fr07-attachments-payload.md`
- [ ] Decision: Phase E required (YES) or skipped (NO)

**Critical Tasks:**
- `001` — FR-07 spike (MUST BE FIRST; unblocks payload-wiring task 026 and conditional Phase E)

**Inputs**: [`src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs), OpenAPI schema, BFF Swagger.

**Outputs**: Spike memo with decision + spike code (if any) in `notes/spikes/`.

---

### Phase A: Foundations

**Objectives:**
1. Establish shared primitives that all three pane refactors depend on.
2. Make tab limit and telemetry centrally configurable.

**Deliverables:**
- [ ] `<PaneHeader>` component published from `@spaarke/ui-components` with `title`, `icon`, `rightSlot` props (FR-01)
- [ ] `MAX_WORKSPACE_TABS = 8` constant + FIFO eviction logic in `WorkspaceTabManager.ts` (FR-13)
- [ ] `ActionCard` accessible from `@spaarke/ui-components` (extract if needed; otherwise verified context-agnostic) per ADR-012 (A-3)
- [ ] Error-only telemetry helper module (`logTelemetryError(eventName, properties)`) wired to App Insights (FR-24)

**Critical Tasks:**
- `010` — Create `<PaneHeader>` (blocking for B/C/D)
- `012` — ActionCard lift/verify (blocking for D task 041)

**Inputs**: Spec FR-01, FR-13, FR-24; ADR-012, ADR-021, ADR-025.

**Outputs**: 4 deliverables above; updated `@spaarke/ui-components` exports.

---

### Phase B: Assistant Pane

**Objectives:**
1. Reduce welcome chrome to "How can I help you today?" + recent conversations.
2. Activate input on cold load; lazy session creation.
3. Replace tabs with History overlay; restructure toolbar; enable multi-file attach.

**Deliverables:**
- [ ] WelcomePanel chrome trimmed (no sparkle, no heading, no 4 prompt cards) (FR-04, FR-05)
- [ ] ConversationPane uses `<PaneHeader title="Assistant" icon={ChatRegular}>` (FR-02)
- [ ] HistoryOverlay component + History button + session loading via `setChatSessionId` (FR-03)
- [ ] SprkChatInput editable on cold load; lazy session creation via `useChatSession` (FR-06)
- [ ] `useChatFileAttachment` hook with multi-file picker + lazy PDF.js/mammoth extraction (FR-07 frontend)
- [ ] SprkChat toolbar: prompt menu + `+` strip above input; remove Export Word (FR-08, FR-09)
- [ ] Attachment chips + payload wiring into chat send (FR-07 wiring; depends on spike outcome)

**Critical Tasks:**
- `024` — `useChatFileAttachment` hook (blocks 026)
- `026` — payload wiring (blocked by 001 spike result)

**Inputs**: [`ConversationPane.tsx`](../../src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx), [`WelcomePanel.tsx`](../../src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx), [`SprkChat.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx), `<PaneHeader>` from Phase A.

**Outputs**: 7 modified or net-new files; ConversationPane simplified; SprkChat toolbar restructured.

---

### Phase C: Workspace Pane

**Objectives:**
1. Embed LegalWorkspace as default "Home" tab via `WorkspaceShell` reuse.
2. Replace tab bar with `WorkspacePaneMenu` (Open / Home / Switch Workspace / Edit).
3. Add Daily Briefing as a new LegalWorkspace section type with 429/empty handling.

**Deliverables:**
- [ ] WorkspacePane uses `<PaneHeader title="Workspace" icon={AppsListRegular}>` + embeds `WorkspaceShell` as Home tab (FR-10, FR-11)
- [ ] `WorkspaceLandingWidget.tsx` deleted
- [ ] `WorkspacePaneMenu` Fluent v9 Dropdown component (Open / Home / Switch / Edit) (FR-12)
- [ ] `WorkspaceLayoutWizard.templateFilter` prop — SpaarkeAi passes 6-template subset; standalone passes nothing (FR-14)
- [ ] Daily Briefing section + `useDailyBriefing` hook + registration (FR-15)
- [ ] Daily Briefing 429 graceful degradation + empty state ("Nothing to see right now — enjoy your day") (FR-16, NFR-11)

**Critical Tasks:**
- `030` — WorkspacePane + LegalWorkspace embed (foundational for C)
- `034` — Daily Briefing section (blocks 035 empty state)

**Inputs**: [`WorkspacePane.tsx`](../../src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx), [`WorkspaceTabManager.ts`](../../src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts), [`WorkspaceLayoutWizard/src/App.tsx`](../../src/solutions/WorkspaceLayoutWizard/src/App.tsx), [`sectionRegistry.ts`](../../src/solutions/LegalWorkspace/src/sectionRegistry.ts), `<PaneHeader>` + `MAX_WORKSPACE_TABS` from Phase A.

**Outputs**: 6 modified/net-new files; Workspace pane composes LegalWorkspace; new Daily Briefing section available in BOTH standalone LegalWorkspace and SpaarkeAi.

---

### Phase D: Context Pane

**Objectives:**
1. Migrate ContextPaneController to shared `<PaneHeader>`.
2. Replace `PlaybookGalleryWidget` on welcome stage with new `GetStartedCardsWidget` (7 cards).
3. Route card clicks through `PaneEventBus` `workspace` channel → wizards open as new tabs.

**Deliverables:**
- [ ] ContextPaneController uses `<PaneHeader>`; stage label "Gallery" → "Get Started" on welcome (FR-17, FR-22)
- [ ] `GetStartedCardsWidget` — 7 cards in 2-col scrollable grid (FR-18)
- [ ] Widget registered in `ContextWidgetRegistry`; welcome stage swaps to it; `PlaybookGalleryWidget` retained for non-welcome stages (FR-21)
- [ ] Wizard widget wrappers: `CreateProjectWizardWidget`, `FindSimilarWizardWidget` (FR-19)
- [ ] Analysis Builder intents: `email-compose`, `meeting-schedule` registered as wizard widgets (FR-19)
- [ ] `AssignWorkWizardLauncher` — `Xrm.Navigation.navigateTo` thin dispatcher + Vite-dev placeholder (FR-20)

**Critical Tasks:**
- `041` — GetStartedCardsWidget (depends on `ActionCard` from Phase A task 012)
- `042` — registration + stage swap (depends on 041)

**Inputs**: [`ContextPaneController.tsx`](../../src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx), [`ContextWidgetRegistry.ts`](../../src/client/shared/Spaarke.AI.Widgets/src/registry/ContextWidgetRegistry.ts), `ActionCard` + `<PaneHeader>` from Phase A.

**Outputs**: 6 modified/net-new files; Context pane welcome state is now the 7-card grid; wizards route to Workspace pane.

---

### Phase E: Backend (Conditional)

**Objectives** (only if Phase 0 spike confirms gap):
1. Extend `POST /api/ai/chat/sessions/{sessionId}/messages` to accept `attachments[]` field.
2. Validate attachment array (max 5, max 10 MB per file, allowed MIME types).
3. Pass through `attachments[]` to LLM call context.

**Deliverables (CONDITIONAL):**
- [ ] `attachments[]` schema in `ChatEndpoints.cs` POST request DTO
- [ ] Validation: max 5 entries, max 10 MB per file, allowed MIME types (`text/plain`, `text/markdown`, `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`)
- [ ] BFF unit tests for happy path + each validation rejection

**Inputs**: Phase 0 spike memo, [`ChatEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs), ADR-008, ADR-013.

**Outputs**: Modified endpoint contract; new unit tests passing.

---

### Phase F: Integration & Verification

**Objectives:**
1. Verify auth contract across new code.
2. Verify bundle-size budget held.
3. Verify dark-mode token compliance.
4. Verify backwards compatibility with standalone LegalWorkspace.

**Deliverables:**
- [ ] Auth audit: grep for `accessToken` props / state snapshots in new components; all fetches via `authenticatedFetch` (FR-23, NFR-08)
- [ ] Bundle-size verification via webpack-bundle-analyzer: <250 KB gzip delta vs R2 baseline; PDF.js + mammoth absent from initial bundle (NFR-12)
- [ ] Dark-mode token audit: lint rule + manual side-by-side passes; no `#` literals, no `rgba(...)` literals in new code (NFR-06, ADR-021)
- [ ] Backwards-compat verification: standalone LegalWorkspace opened in separate tab → all 9 templates in wizard, existing user layouts render identically (FR-25, NFR-10)

**Inputs**: Built artifacts from Phases B/C/D (+ E if applicable).

**Outputs**: Verification reports in `notes/` for each gate; any failures opened as fix tasks before Phase G.

---

### Phase G: Deploy + Smoke

**Objectives:**
1. Deploy SpaarkeAi production bundle to Dataverse via existing script.
2. Smoke-test each pane in a real Power Apps host environment.
3. Verify NFR-01..NFR-03 performance targets.

**Deliverables:**
- [ ] Deployed via `scripts/Deploy-SpaarkeAi.ps1` (success in deploy log)
- [ ] Assistant pane smoke (FR-02..FR-09 acceptance criteria) via `ui-test` skill
- [ ] Workspace pane smoke (FR-10..FR-16) via `ui-test` skill
- [ ] Context pane smoke (FR-17..FR-22 incl. ADR-021 dark mode check) via `ui-test` skill
- [ ] NFR verification: Lighthouse pane render <500 ms; History overlay open <200 ms; session list populate <300 ms p95 (NFR-01, NFR-03)

**Inputs**: Built artifacts from Phase F; verified deploy script; running BFF.

**Outputs**: Deployed Code Page; smoke reports in `notes/`; sign-off on FR + NFR acceptance.

---

### Phase H: Wrap-up

**Objectives:**
1. Capture lessons learned, update README, archive.

**Deliverables:**
- [ ] `notes/lessons-learned.md` (5–10 lessons covering: PaneHeader lift mechanics, FR-07 attachment design choices, LegalWorkspace embed pattern, ActionCard reuse, layout templateFilter prop pattern, telemetry helper boundary, any unexpected regressions)
- [ ] README status → Complete (with completion date)
- [ ] PR description updated with final shippable status; merge readiness

**Inputs**: All preceding phases.

**Outputs**: lessons-learned.md, completed README, merge-ready PR.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| `pdfjs-dist` (PDF text extraction) | Not installed | Medium | Add as new npm dep; lazy-load on first `+` click |
| `mammoth` (DOCX text extraction) | Not installed | Medium | Add as new npm dep; lazy-load; verify size in NFR-12 task |
| `Xrm.Navigation.navigateTo` (Power Apps host) | Available in host | High in Vite dev | Feature-detect; placeholder for non-host envs |
| App Insights connection | Wired in R2 | Low | Reuse existing telemetry channel; add custom event names only |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `@spaarke/ui-components` (shared component library) | `src/client/shared/Spaarke.UI.Components/` | Active; will receive new `<PaneHeader>` |
| `@spaarke/auth` (auth v2 function-based) | `src/client/shared/Spaarke.Auth/` | Active; consumed via `authenticatedFetch` |
| `WorkspaceShell`, `useWorkspaceLayouts` | `src/solutions/LegalWorkspace/` | Active; embedded as-is |
| `WorkspaceLayoutWizard` | `src/solutions/WorkspaceLayoutWizard/` | Active; receives optional `templateFilter` prop |
| `PaneEventBus`, `AiSessionProvider`, widget registries | `src/client/shared/Spaarke.AI.Widgets/` | Active; consumed by new widgets |
| BFF endpoints (`/api/ai/chat/sessions`, `/api/ai/daily-briefing/narrate`, `/api/workspace/layouts/*`) | `src/server/api/Sprk.Bff.Api/Api/` | Active in production |
| `CreateMatterWizardWidget`, `DocumentUploadWizardWidget` (existing wizard widgets) | `Spaarke.AI.Widgets` registries | Active; referenced by GetStartedCardsWidget |

---

## 6. Testing Strategy

**Unit Tests** (no formal coverage target; spec-defined):
- New hooks: `useChatFileAttachment` (file extraction, error paths), `useDailyBriefing` (429 handling, empty state)
- `<PaneHeader>` snapshot test (icon + title + rightSlot rendering)
- `WorkspacePaneMenu` interaction tests (close button only on non-Home, FIFO eviction logic)
- `WorkspaceLayoutWizard.templateFilter` filtering correctness (passes through → 6 templates; absent → 9 templates)

**Integration Tests**:
- Phase 0 spike validates backend payload contract via direct API call
- BFF unit tests for `attachments[]` validation (Phase E, conditional)
- Auth `authenticatedFetch` integration verified via end-to-end token refresh test in deployed env

**E2E / UI Smoke Tests** (Phase G via `ui-test` skill):
- Assistant pane cold load → input editable, no chrome elements forbidden by FR-04
- Multi-file attach (5 files, 1 corrupted PDF) → 4 chips + 1 error chip → send → AI references all 4
- Workspace pane Home tab + open 9 widgets → FIFO eviction; tabs persist across page refresh (NFR-09)
- Workspace pane Daily Briefing 429 → graceful card with retry; empty state matches OC-08
- Context pane → click each of 7 cards → correct wizard opens as new tab in Workspace
- Assign Work in Power Apps host → custom page navigates; in Vite dev → placeholder shown
- Dark mode toggle → no color regressions in new components (ADR-021)
- Standalone LegalWorkspace separate tab → 9 templates in wizard, existing layouts unchanged (FR-25)

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase A:**
- [ ] `<PaneHeader>` exported from `@spaarke/ui-components` index
- [ ] `MAX_WORKSPACE_TABS = 8` constant exported; FIFO eviction logic exempts Home
- [ ] Telemetry helper emits to App Insights with custom event names

**Phase B:**
- [ ] All 8 FR-02..FR-09 manual smoke acceptance criteria pass
- [ ] PDF.js + mammoth absent from initial bundle (verified via bundle-analyzer)

**Phase C:**
- [ ] All 7 FR-10..FR-16 manual smoke acceptance criteria pass
- [ ] WorkspaceLandingWidget deleted (no orphan import warnings)
- [ ] Daily Briefing 429 → graceful card; empty → "Nothing to see right now — enjoy your day"

**Phase D:**
- [ ] All 6 FR-17..FR-22 manual smoke acceptance criteria pass
- [ ] Click each of 7 GetStarted cards → correct wizard opens as new Workspace tab
- [ ] Assign Work feature-detect works in Vite dev (placeholder)

**Phase E (conditional):**
- [ ] BFF endpoint accepts `attachments[]` with max-5 cap, MIME validation, 10 MB cap
- [ ] Unit tests cover happy path + each rejection

**Phase F:**
- [ ] Zero `accessToken` props or state in new components
- [ ] Bundle delta <250 KB gzip vs R2 baseline
- [ ] Zero hex/rgba literals in new code
- [ ] Standalone LegalWorkspace identical to before

**Phase G:**
- [ ] Deployed successfully via `Deploy-SpaarkeAi.ps1`
- [ ] All UI smoke tests pass in Power Apps host
- [ ] NFR-01 / NFR-03 timings measured and pass

### Business Acceptance

- [ ] User can complete the "arrive on welcome state and start any of 7 wizards" flow within 2 clicks
- [ ] Standalone LegalWorkspace users see no regression
- [ ] Telemetry surfaces all 3 error event types in App Insights when manually triggered

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R-1 | Backend chat-message endpoint doesn't accept `attachments[]` — requires extension | Medium | Medium | Phase 0 spike (task 001) determines; Phase E conditionally unlocks |
| R-2 | `Xrm.Navigation.navigateTo` unavailable in Vite dev | High (in dev) | Low | Feature-detect `window.Xrm`; placeholder per A-2, FR-20 |
| R-3 | `ActionCard` not context-agnostic; lift to shared lib needed | Low | Low | Task 012 verifies; refactor if needed before D phase |
| R-4 | Daily Briefing endpoint lacks per-user caching → tab-switch spam | Medium | Low | Verify in UQ-02; add frontend cache in `useDailyBriefing` if needed |
| R-5 | Non-Home tabs not persisted by R2 `SessionPersistenceService` | Low | Medium | Verify in Phase F task 063; opens BFF/Cosmos sub-task if surfaced (per UQ-03) |
| R-6 | PDF.js + mammoth combined bundle exceeds 250 KB gzip | **MATERIALIZED (Option 1 accepted)** | Medium | **Realized 2026-05-20**: post-Wave 3 SpaarkeAi bundle = **798 KB gzip** (vs ~508 KB target = ~290 KB overrun). Source-level lazy-loading is correctly implemented (`await import('pdfjs-dist')` / `await import('mammoth')`) but `vite-plugin-singlefile` (per ADR-026) inlines async chunks into the singlefile HTML, nullifying the bundle benefit. **User accepted Option 1** (ship as-is for R3). Mitigation: Phase G task 074 will empirically validate NFR-01 / NFR-03 timings against the real bundle; if those pass, the overrun is operationally acceptable. Long-term path: Option 2 (separate Dataverse web resources for heavy libs) — deferred to a follow-on project. See [`notes/bundle-size-verification.md`](notes/bundle-size-verification.md) (task 061) + [`docs/assessments/code-page-bundle-size-vs-singlefile-2026-05-20.md`](../../docs/assessments/code-page-bundle-size-vs-singlefile-2026-05-20.md) (cross-project assessment). |
| R-7 | `Xrm.Navigation.navigateTo` `data` query string format wrong for `sprk_createworkassignmentwizard` | Low | Medium | Reference user-provided runtime log; verify in task 045 before going live (UQ-05) |
| R-8 | Bug 2 / ADR-012 cross-cutting limitation — SpaarkeAi Home tab shows structural placeholders only because section infrastructure lives solution-local in LegalWorkspace | **RESOLVED (2026-05-20 / task 067 + 2026-05-21 / task 069)** | Medium | **Architecturally resolved (task 067)**: builder + types hoisted to `@spaarke/ui-components`. **Default-case visual fix (task 069)**: per operator's Option Z direction, SpaarkeAi Home tab now defaults to Daily Briefing as its single section content. Daily Briefing infrastructure (hook + section + registration factory) was hoisted to `@spaarke/ui-components` because its data layer is a pure BFF AI call (no Dataverse entity strings, so ADR-012 permits the hoist). Other 5 legal-domain factories correctly stay solution-local. SpaarkeAi Home tab now shows real Daily Briefing content (bullets / 429 card / empty state) — Bug 2 visual fix landed for the default case. Future multi-section Home expansion (recent AI sessions, etc.) is deferred to next review. See [`notes/067-shared-lib-hoist-summary.md`](notes/067-shared-lib-hoist-summary.md) + [`notes/deploys/2026-05-20-deploy.md`](notes/deploys/2026-05-20-deploy.md) task 069 supplemental section. |
| R-9 | Bug 1 — Assistant pane has no chat input on cold load (welcome/SprkChat ternary mounts one or the other, never both) | **RESOLVED (2026-05-20 / task 068)** | High | **Resolved**: `ConversationPane.tsx` restructured so `SprkChat` is ALWAYS rendered; the welcome heading (now a thin shell in `WelcomePanel.tsx`) sits above it when `showWelcomePanel===true`. FR-06 (input editable on cold load) is functionally true. Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Task 068". |
| R-10 | UX-A — "Recent Conversations" section duplicates History overlay (two ways to resume past sessions; operator confusing) | **RESOLVED (2026-05-20 / task 068)** | Low | **Resolved**: `RecentConversationsSection` + `useRecentSessions` removed from `WelcomePanel.tsx`. Session resume now exclusively via PaneHeader history icon → `HistoryOverlay` (task 022 pattern). |
| R-11 | UX-B — Create Matter + Summarize Files cards on Get Started inconsistent with other 5 (embedded Workspace tab vs Code Page popup) | **RESOLVED (2026-05-20 / task 068)** | Low | **Resolved**: `ContextPaneController.handleGetStartedCardClick` now routes `create-matter-wizard` → `sprk_creatematterwizard` popup and `document-upload-wizard` → `sprk_summarizefileswizard` popup via Xrm.Navigation (mirrors `launchAssignWorkWizard` pattern). The embedded `CreateMatterWizardWidget` + `DocumentUploadWizardWidget` remain registered in `WorkspaceWidgetRegistry` for non-ContextPane consumers. All 7 cards now use the popup pattern consistently. |
| R-12 | R3-introduced bug — `WorkspacePaneMenu` (task 032) Switch Workspace section silently empty in deployed SpaarkeAi (operator smoke round 2). Module-level `authenticatedFetch` + `getBffBaseUrl()` raced runtime-config bootstrap; `getBffBaseUrl()` threw on first effect run, the catch block swallowed it, and the effect's dep array had no readiness signal to re-fire on, leaving the menu permanently empty. | **RESOLVED (2026-05-21 / task 081)** | Medium | **Resolved**: `WorkspacePaneMenu.tsx` rewired to consume `useAiSession().authenticatedFetch` + `useAiSession().bffBaseUrl` + `useAiSession().isAuthenticated` (matches the R3 pattern used by `WorkspaceHomeTab`, `HistoryOverlay`, `useDailyBriefing`). The internal `useWorkspaceLayoutsList` hook now defers (`return early`) when `!isAuthenticated || !bffBaseUrl`, then auto-runs once those flip truthy. Fetch failures now log to `console.error` (not silently swallowed). `launchWizard` accepts `bffBaseUrl` as a parameter instead of calling module-level `getBffBaseUrl()`. FR-25 preserved — standalone LegalWorkspace untouched (this is a SpaarkeAi-local component). Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Task 081 (WorkspacePaneMenu fetch fix)". |
| R-13 | SprkChat UX bugs from operator smoke round 2 — (a) cursor focus did not return to chat input after the response stream completed, forcing the operator to click back into the textarea before each follow-up; (b) the strip-mounted Prompt menu (`PromptRegular` button in the toolbar above the input, FR-09 / task 025) had no way to close — clicking outside the floating SlashCommandMenu did nothing, and the menu had no built-in dismiss-on-outside-click handler. | **RESOLVED (2026-05-21 / task 082)** | Low | **Resolved**: (a) `SprkChatInput.tsx` now tracks the previous `disabled` value via a `useRef` and, on the `true → false` transition (i.e., streaming complete), schedules `inputRef.current?.focus()` via `requestAnimationFrame` so layout/aria changes settle before focusing. Local to the input — no new prop wiring needed. (b) `SlashCommandMenu.tsx` now adds the proven document-level `mousedown` outside-click handler (mirrors `SprkChatActionMenu.tsx` lines 356–376) — clicks outside the menu container AND outside `anchorRef` AND outside the strip-mounted Prompt button (`data-testid="strip-prompt-menu-button"`, allow-listed to prevent open/close race with the toolbar button's own onClick) call `onDismiss`. Esc-to-close + Fluent v9 token compliance preserved. Standalone LegalWorkspace untouched. Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Task 082 (SprkChat UX fixes: focus + menu close)". |
| R-15 | Parallel implementation of workspace layouts fetch — task 081 introduced an inline `useWorkspaceLayoutsList` hook in `WorkspacePaneMenu.tsx` instead of reusing LegalWorkspace's working `useWorkspaceLayouts` hook. Although task 081 fixed the config-not-ready race, the parallel implementation diverged from the working pattern (no cache-first hydration, no sessionStorage layout cache, simpler selection cascade) and the Switch Workspace dropdown remained intermittently empty in deployed SpaarkeAi. Violates operator reuse principle: "when we have working components reuse them." | **RESOLVED (2026-05-21 / task 084)** | Medium | **Resolved**: created `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts` — a faithful SpaarkeAi adaptation of LegalWorkspace's WORKING `useWorkspaceLayouts` hook (cache-first hydration, parallel list+default fetch, pinned-id resolution, setActiveLayoutById, refetch). Three surgical adaptations documented in the hook header: (a) auth from `useAiSession()` not module-level imports (preserves task 081 root-cause fix); (b) drops LayoutJson parsing (menu only needs list + active ID); (c) drops SYSTEM_DEFAULT_LAYOUT fallback (menu degrades to "No workspaces available" empty state instead of fake stub). `WorkspacePaneMenu.tsx` now imports + consumes this hook; the inline `useWorkspaceLayoutsList` (~125 lines) was removed. Option C (Copy) chosen over Option H (Hoist) because the LegalWorkspace hook is coupled to its section registry (`LayoutJson`); clean hoist would risk FR-25 standalone build. JSDoc cross-references the LegalWorkspace source so both stay in sync. Standalone LegalWorkspace untouched (569.22 KB gzip — identical to task 070 baseline). SpaarkeAi bundle 800.07 KB gzip (+2.35 KB vs pre-fix). Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Round 4 Fix 1". |
| R-14 | BFF brittleness on missing Dataverse data — operator smoke round 2 surfaced two cascading failures in the dev environment: (a) `DataverseCapabilityManifestLoader.LoadAsync` threw `InvalidOperationException` on every `ManifestRefreshService` tick because the `sprk_aicapability` table is not provisioned in dev, which propagated through `DetectToolCallsAsync` → `ChatEndpoints.SendMessage`'s outer catch → SSE error event → frontend "there was an issue" generic error; (b) `DailyBriefingEndpoints.HandleNarrate` returned 400 ProblemDetails on the frontend's standard empty-state probe (`{categories:[],priorityItems:[],channels:[]}` sent by `useDailyBriefing` when no notifications are visible), forcing the hook into its 400-special-case branch and surfacing as a misleading "Bad Request" in App Insights. Both issues blocked end-to-end smoke validation of chat (FR-02..FR-09) and Daily Briefing (FR-15..FR-16) in the fresh dev environment. | **RESOLVED (2026-05-21 / task 083)** | High | **Resolved (BFF in-process only — ADR-013 compliant)**: (a) `DataverseCapabilityManifestLoader.LoadAsync` now detects "missing entity" Dataverse responses (404 NotFound, or 400 with body containing "Resource not found for the segment" / "Could not find a property named" / "does not exist") via a new internal `IsMissingEntityResponse` helper. On match, returns `Array.Empty<CapabilityManifestEntry>()` and logs a `Warning` (not Error) so chat tool detection runs with zero tools available — the rest of the chat flow proceeds normally. Other failure modes (5xx transient, 401, malformed 400) still throw so `ManifestRefreshService` stale-on-error policy retains the existing manifest. Same tolerance applied to `PlaybookService.ExecuteListQueryAsync` (count + select queries both check). (b) `DailyBriefingEndpoints.HandleNarrate` now returns 200 with `{tldr:{briefing:"",topAction:"",...}, channelNarratives:[]}` when all three actionable arrays are empty (instead of 400) — the `useDailyBriefing` 400-special-case branch is preserved for safety (handles other 400 causes) but is now unreachable for the empty-state probe. Unit tests added: `DataverseCapabilityManifestLoaderTests.IsMissingEntityResponse_*` (7 assertions) + `DailyBriefingEndpointsTests.HandleNarrate_Returns_200_Empty_On_Empty_Payload` (2 facts). BFF deployed to spaarkedev1; `/healthz` 200 verified. Standalone LegalWorkspace untouched. ADR-013 (BFF in-process changes only, no new services), ADR-019 (ProblemDetails preserved for real errors), ADR-008 (endpoint filters preserved) all complied with. See deploy memo "Supplemental Deploy — Task 083 (BFF resilience: missing manifest + empty Daily Briefing)". |
| R-16 | Parallel implementation of wizard launchers — operator smoke (2026-05-21) confirmed wizards work fine when launched from LegalWorkspace getStarted cards but DO NOT open when launched from SpaarkeAi's Context-pane GetStartedCards. Root cause: tasks 042/044/045/068 built parallel implementations (`launchCodePagePopup` helper in `ContextPaneController`, `launchAssignWorkWizard` in `@spaarke/ai-widgets`, plus `widget_load → tab-mount → widget calls navigateTo` two-hop path for create-project / find-similar / email-compose / meeting-schedule). The package-local helpers checked `window.Xrm` only (no frame-walking) while the widget-mount path used frame-walking — explaining why "Create Project worked but the other 6 did not." Operator principle: "when we have working components reuse them." | **RESOLVED (2026-05-21 / task 085)** | High | **Resolved (Option H — Hoist)**: created `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts` exporting `launchCreateMatterWizard`, `launchCreateProjectWizard`, `launchSummarizeFilesWizard`, `launchFindSimilarWizard`, `launchAssignWorkWizard`, `launchPlaybookIntent`. Each launcher uses the VERBATIM navigateTo call shape from LegalWorkspace's `WorkspaceGrid.tsx` (handleOpenWizard, handleOpenProjectWizard, handleOpenSummarize, handleOpenFindSimilar, handleOpenWorkAssignmentWizard) and `ActionCardHandlers.ts` (openPlaybookIntent) — same `pageType:"webresource"`, same `target:2`, same `60% × 70%`, same `bffBaseUrl=<encoded>` data shape, same titles ("Create New Matter", "Summarize Files", "Find Similar Documents", "Create Work Assignment", "Playbook Library"). Adds **frame-walking** Xrm resolution so the launchers work regardless of iframe nesting depth (the missing piece in the package-local `launchCodePagePopup`). `ContextPaneController.handleGetStartedCardClick` now routes all 7 cards directly through the shared launchers — no more `widget_load → tab-mount` round-trip, no more package-local `launchCodePagePopup`. Embedded workspace widgets (`CreateProjectWizardWidget`, `FindSimilarWizardWidget`, `EmailComposeWidget`, `MeetingScheduleWidget`, `CreateMatterWizardWidget`, `DocumentUploadWizardWidget`) REMAIN REGISTERED in `WorkspaceWidgetRegistry` per operator's "keep embedded structure for future use cases" directive — only the welcome-state card-click routing changes. Deleted: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/AssignWorkWizardLauncher.ts` + its unit test (superseded by the shared launcher). LegalWorkspace's local handlers in `WorkspaceGrid.tsx` + `ActionCardHandlers.ts` untouched for FR-25 byte-stability (bundle 569.22 KB gzip — identical to baseline). SpaarkeAi bundle 800.56 KB gzip (slightly smaller post-fix). Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Round 4 Fix 2 (Wizards via reused LegalWorkspace handlers)". |
| R-17 | Daily Briefing auto-load empty-payload contract gap — operator smoke (2026-05-21) confirmed Daily Briefing renders correctly in the standalone Daily Briefing Code Page (`sprk_dailybriefing`) but renders the "Nothing to see right now — enjoy your day" empty state in the SpaarkeAi Workspace Home tab on cold load, even when the user has real unread `appnotification` records. Root cause: task 069's `WorkspaceHomeTab` auto-fired the shared `useDailyBriefing` hook with `buildEmptyNarrateRequest()` (empty categories / priorityItems / channels) because the shared hook had no programmatic source for notification context. Task 083 made the BFF return 200/empty bullets on the empty envelope (instead of 400) — which prevented the misleading "Bad Request" in App Insights but ALSO meant SpaarkeAi never received real bullets on its standard auto-load path. The standalone Daily Briefing Code Page works because it queries `appnotification` via Xrm.WebApi on mount and builds a populated payload (`useNotificationData` → `briefingService.buildNarrationRequest`); SpaarkeAi had no equivalent data path. Operator principle: "all of this needs to use all of our existing auth, bff.api connectors, etc." + "REUSE the working code from the standalone Daily Briefing." | **RESOLVED (2026-05-21 / task 086)** | High | **Resolved (Hoist optional callback + Copy standalone data path)**: (1) shared `useDailyBriefing` hook now accepts an optional `loadNotificationContext: () => Promise<NarrateRequest \| null>` parameter; when supplied, the hook invokes it before POST-ing to narrate and forwards the populated payload verbatim (when omitted, the legacy empty-payload contract is preserved for LegalWorkspace's shim — preserves FR-25). Symmetric prop added to `DailyBriefingSection` + `createDailyBriefingRegistration` factory. `NarrateRequest` + 4 sub-types exported from the shared lib barrel for consumer typing. (2) Created `src/solutions/SpaarkeAi/src/services/notificationContextLoader.ts` — a faithful SpaarkeAi-local copy of the standalone Daily Briefing Code Page's working data path (Xrm frame-walk, `appnotification` query via Xrm.WebApi with same `$select` columns + `$orderby createdon desc` + `$top=200`, same `customData` JSON parsing, same `groupByCategory` logic, same `buildNarrationRequest` payload assembly). Option C (Copy) chosen over Option H (Hoist) of the loader logic because the standalone's `useNotificationData` tangles together notifications + preferences + mark-as-read + refresh actions that SpaarkeAi's Home tab doesn't need; clean hoist would require splitting that hook apart (out of scope for a fix task). The copy carries JSDoc cross-references to the source files for sync. ADR-012 satisfied: Dataverse entity strings (`appnotification`, `customData`, `toasttype`) all live in the solution-local loader; the shared lib's new parameter is a generic callback. (3) `WorkspaceHomeTab.tsx` now passes `loadNotificationContext: loadSpaarkeAiNotificationContext` to the registration factory — same Xrm.WebApi query the standalone uses, same populated payload to the BFF, same real AI bullets in the response. Edge cases handled: Xrm unavailable (dev/Vite) → loader returns null → hook falls back to empty payload → empty-state UI (no error); zero notifications → loader returns null → empty-state UI (correct); loader throws → hook catches + warns + falls back to empty payload (no crash). LegalWorkspace `dailyBriefing.registration.ts` shim untouched — runtime behavior preserved (FR-25 / NFR-10); only +140 bytes of optional-param forwarding code path that's `undefined` at runtime for LegalWorkspace (569.29 KB gzip vs 569.22 KB baseline). Standalone Daily Briefing Code Page untouched. SpaarkeAi bundle 801.85 KB gzip (+1.78 KB vs task 084 baseline). ADR-013 (no new BFF service), ADR-014 (5-min TTL cache preserved), ADR-016 (429 graceful), ADR-028 (auth contract preserved) all complied with. Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Round 4 Fix 3 (Daily Briefing functional in SpaarkeAi)". |
| R-19 | Embed wiring gaps (initialWorkspaceId / displayName / runtime config) — operator smoke after task 087 (2026-05-21, round 4) surfaced three bugs in the freshly embedded LegalWorkspaceApp path. **Bug 1**: each `Switch Workspace` selection opened a new tab with the same "last-active" workspace because LegalWorkspace's `useWorkspaceLayouts` cache-hydrated from a sessionStorage key shared across all tabs — `initialWorkspaceId` was effectively ignored on cache hit. **Bug 2**: every workspace tab title rendered as "Workspace" (the registry metadata's generic label) instead of the chosen layout name — `WorkspacePaneMenu.handleLayoutSelect` placed the friendly name in `widgetData.layoutName` only, not at the top level of the event, so `WorkspacePane.tsx`'s displayName-precedence ladder fell through to registry metadata. **Bug 3**: clicking "preview" on a document inside the embedded tree threw "[LegalWorkspace] Runtime config not initialized. Call initRuntimeConfig() in main.tsx before using getBffBaseUrl()." — LegalWorkspace has its own `runtimeConfig` singleton (separate from SpaarkeAi's) and SpaarkeAi's `main.tsx` only initialized its own. | **RESOLVED (2026-05-21 / task 088, Round 4 Fix 4.1)** | High | **Resolved (three surgical fixes)**: **Bug 1**: added new `embedded?: boolean` option to `useWorkspaceLayouts` via a backwards-compatible overload (legacy `useWorkspaceLayouts(initialWorkspaceId)` still works; new `useWorkspaceLayouts({ initialWorkspaceId, embedded })` is the embedded call shape). When `embedded=true`, the hook SKIPS `sessionStorage` reads AND writes — both the cache-hydration block at the start of the fetch effect and the cache-write at the end of the BFF resolution path; `setActiveLayoutById` likewise stops mirroring to cache. Wired through `WorkspaceGrid` (`embedded` forwarded as a prop) and `LegalWorkspaceApp` (passes `embedded` to `WorkspaceGrid`). **Bug 2**: `WorkspacePaneMenu.handleLayoutSelect` now dispatches `widget_load` with `displayName: layoutName` at the TOP LEVEL of the event (not just inside `widgetData`); `WorkspacePane.tsx`'s existing displayName-precedence ladder (event.displayName → registry → widgetType) picks it up correctly. **Bug 3** (Option A, dual-init): re-exported `setRuntimeConfig as setLegalWorkspaceRuntimeConfig` from LegalWorkspace's barrel (`src/solutions/LegalWorkspace/src/index.ts`); SpaarkeAi's `main.tsx` calls it with the SAME `IRuntimeConfig` immediately after its own `setRuntimeConfig(config)`. Both singletons remain distinct in-process instances holding equivalent values for `bffBaseUrl` / `scope` / `clientId` / `tenantId`. ADR-028 / FR-25 / NFR-10 all preserved: standalone LegalWorkspace runtime entry (`main.tsx → App.tsx`) still calls its own internal `setRuntimeConfig` and never imports the barrel; the embedded path on its own does not change standalone code paths. LegalWorkspace bundle 569.30 KB gzip (+80 bytes structural; standalone never executes the new conditional branches). SpaarkeAi bundle 893.75 KB gzip (−0.16 KB vs task 087 baseline). Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Task 088 (embed wiring fixes 4.1)". |
| R-20 | WorkspacePaneMenu UX clutter — the dropdown's `Open` (per-tab list with close affordance) and `Home` sections duplicated UI already visible above the menu (the open tab bar) and reified a "Home" concept that had been retired (Daily Briefing is now just one widget, not a special home tab). Operator feedback (2026-05-21): "we don't need the Open and Home areas/links — these don't add UI value". | **RESOLVED (2026-05-21 / task 089)** | Low | **Resolved**: `WorkspacePaneMenu.tsx` cleaned up — removed the Open + Home sections (along with their derived memos `homeTab` / `openTabs`, the `handleTabActivate` / `handleTabCloseClick` callbacks, the `tabRow` / `closeButton` style entries, and the `DismissRegular` / `HomeRegular` icon imports). Renamed the `Switch Workspace` MenuGroupHeader to `Select Workspace` per operator wording. Added a new `Manage workspaces` menu entry (stub for task 093 — onClick calls `console.log('Manage workspaces — task 093 will implement side pane')`; uses `SettingsRegular` icon). `Edit current workspace` kept (operator did not request removal; remains a meaningful action on the active layout). New visual hierarchy: `Select Workspace` (list of layouts) → `MenuDivider` → `+ New Workspace` / `Manage workspaces` / `Edit current workspace`. `WorkspacePaneMenuProps` left structurally identical — `tabs` / `activeTabId` / `onTabSelect` / `onTabClose` retained (with the latter two marked optional) to keep the `WorkspacePane.tsx` call site untouched. ADR-021 compliant (Fluent v9 tokens only — no new hex/rgba). FR-25 preserved (standalone LegalWorkspace untouched). SpaarkeAi bundle 893.43 KB gzip (−0.32 KB vs task 088 baseline). Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Task 089 (dropdown cleanup)". |
| R-21 | Workspace builder constraints from operator smoke (round 5, 2026-05-21) — (a) the Arrange Sections step auto-fills every layout slot on first entry and the wizard appears to require all slots to remain filled to save; (b) once a section is placed in a slot there is no affordance to remove it (drag-out to the unassigned pool works in theory but is undiscoverable, and operator's reported behavior was that sections couldn't be removed at all); (c) creating a workspace from the builder offers no way to mark it as "pin to start" so that SpaarkeAi auto-opens it on next cold load — the auto-open behavior itself is deferred to task 092 but the UI flag must be present in task 091 so the operator can set it before task 092 ships. | **RESOLVED (2026-05-21 / task 091)** | Medium | **Resolved (three surgical fixes in WorkspaceLayoutWizard + a one-line guard in the shared renderer)**: (a) confirmed that Step 3's `canAdvance` only required `workspaceName.trim().length > 0` — no all-slots-required gate ever existed, but empty slots produced `console.warn("Unknown section ID: , skipping")` from `buildDynamicWorkspaceConfig` because the wizard serializes empty slots as `""` in `sectionsJson`. Added a guard: `if (!sectionId) { continue; }` BEFORE the registration lookup so empty slots are silently skipped (Fix 1). (b) `GridSlot` now renders a small `DismissRegular` X button in its top-right corner when the slot is filled — visible on hover and on focus (a11y-friendly via `:focus` opacity rule); clicking removes the section from the slot (`onRemove` callback in `ArrangeStep`), the section returns to the unassigned pool automatically via the `unassignedSections` derived state; mousedown `stopPropagation` + `draggable=false` prevent the button from initiating an HTML5 drag (Fix 2). (c) added `pinToStart` boolean state to `App.tsx` + `pinToStart` / `onPinToStartChange` props on `ArrangeStep` + a new Fluent v9 `Checkbox` with `PinRegular` icon and label `"Pin to Start"` in Step 3's inline row next to `Set default`. Persistence is a sessionStorage stub keyed `spaarke:workspace:pinned-layout-id` (only one workspace pinned at a time — matches the eventual BFF single-pinned semantics). The save handler now persists pinToStart on success and the `__dialogResult` carries the flag back to the caller. The BFF DTO is NOT extended in this task — task 092 will add `IsPinned` + `sprk_workspacelayout.sprk_ispinned` and replace the sessionStorage stub with BFF-backed persistence; this is documented as a TODO in `App.tsx` (Fix 3). FR-25 preserved: standalone LegalWorkspace's `WorkspaceGrid.handleOpenWizard` passes neither `templateFilter` nor `pinToStart` — both default gracefully (`templateFilter` undefined → all templates shown per task 033; `pinToStart` initial state `false` because `mode==='create'` → no layoutId yet → `readPinToStartForLayout(null)` returns false). LegalWorkspace bundle 569.31 KB gzip (+10 bytes vs 569.30 task 088 baseline — the empty-slot guard's two lines); SpaarkeAi 893.44 KB gzip (+10 bytes vs 893.43 task 089 baseline); wizard 427.58 KB gzip. Deployed to spaarkedev1 — `sprk_workspacelayoutwizard` + `sprk_corporateworkspace` + `sprk_spaarkeai` all updated. ADR-021 (Fluent v9 tokens only — no hex/rgba; uses `tokens.colorNeutralBackground3`, `tokens.colorStrokeFocus2`, `tokens.colorNeutralBackground3Hover`), ADR-028 (auth contract preserved — pinToStart never traverses the BFF in this task), ADR-012 (the `buildDynamicWorkspaceConfig` guard lives in the shared lib where the function already lives — no factory hoist needed) all complied with. See deploy memo "Supplemental Deploy — Task 091 (workspace builder improvements)". |
| R-22 | Tab pinning + auto-open missing — task 091 stubbed pin persistence in `sessionStorage` with a single pin slot, but the operator's intent is multi-pin + auto-open on SpaarkeAi cold load (pinned workspaces should appear as tabs alongside the Home tab whenever the user opens SpaarkeAi), and sessionStorage clears on browser close. Without auto-open, the "Pin to Start" checkbox in the wizard had no visible effect after closing the browser; without multi-pin, only one workspace could ever be pinned at a time. | **RESOLVED (2026-05-21 / task 092)** | Medium | **Resolved (multi-pin localStorage + auto-open effect + per-tab pin icon)**: (a) created `src/solutions/SpaarkeAi/src/services/pinnedWorkspaces.ts` with `getPinnedWorkspaces` / `isPinned` / `pinWorkspace` / `unpinWorkspace` operating over `localStorage` key `spaarke:workspace:pinned-list` (array of `{ layoutId, layoutName }`); all accessors are try/catch-wrapped for private browsing / quota safety. (b) migrated `WorkspaceLayoutWizard/src/App.tsx` from the task 091 single-pin sessionStorage stub to the same multi-pin localStorage shape — wizard and SpaarkeAi share the on-disk schema verbatim (the wizard is a separate Vite app so it cannot import from SpaarkeAi at build time; the shape is replicated with cross-file JSDoc warnings to keep them in sync). (c) added an auto-open mount effect in `WorkspacePane.tsx` — reads the pinned list AFTER `isAuthenticated` is true, filters out any pins whose `layoutId` is already represented in the current tab list (duplicate guard against task 065 tab-restore overlap), and dispatches `widget_load` events with `widgetType: "workspace"`, `widgetData: { layoutId, layoutName }`, `displayName: layoutName` — the same shape `WorkspacePaneMenu.handleLayoutSelect` uses, so the existing pipeline (tab manager → `resolveWorkspaceWidget` → `WorkspaceLayoutWidget`) hydrates each pin without new code paths. Home tab still installs as default; pinned tabs open IN ADDITION (operator decision — user can close Home if they don't want it). A `useRef` guard makes the effect one-shot per mount so `isAuthenticated` toggling on token refresh doesn't re-stack tabs. (d) added a pin icon (`PinRegular` / `PinFilled` from `@fluentui/react-icons`) to `WorkspaceTabManagerComponent.tsx` next to the existing close button — restricted to tabs where `kind === "widget"` AND `widgetType === "workspace"` AND `widgetData.layoutId` is present, so the Home tab and non-workspace widget tabs (Create Matter, Find Similar, etc.) do NOT show the pin affordance. Click toggles localStorage + a local `pinnedIds: Set<string>` React state for synchronous icon flip; pinned state uses `tokens.colorBrandForeground1` for visual prominence; tooltips read "Pin {name} to start" / "Unpin {name} from start"; `aria-pressed` reflects state for screen readers. BFF DTO NOT modified — no user-preferences endpoint exists in `Sprk.Bff.Api` today (the existing `UserPreferences` records in `AiIntentClassificationSchema.cs` / `PlaybookRunContext.cs` are AI-pipeline payloads, not a generic preferences surface). Documented TODO for BFF migration when cross-device sync becomes a requirement. FR-25 preserved — LegalWorkspace untouched (the embedded `LegalWorkspaceApp` only renders inside `WorkspaceLayoutWidget` instances that SpaarkeAi opens; standalone LegalWorkspace doesn't pin anything). SpaarkeAi bundle 894.21 KB gzip (+0.77 KB vs task 091 baseline 893.44); wizard 427.76 KB gzip (+0.18 KB vs 427.58). LegalWorkspace bundle byte-identical to task 091 (no code path touched). ADR-021 (Fluent v9 tokens only — no hex/rgba; `colorBrandForeground1`, `colorNeutralForeground3`, `colorNeutralBackground3Hover`), ADR-022 (React 19 functional), ADR-028 (no auth surface changes — localStorage is client-side state only). Deployed to spaarkedev1 — `sprk_spaarkeai` + `sprk_workspacelayoutwizard` (plus the 7 other wizard pages refreshed by the deploy script). Operator-verifiable: pin a workspace → close browser → reopen SpaarkeAi → pinned workspace auto-opens as a tab. See deploy memo "Supplemental Deploy — Task 092 (tab pinning + auto-open)". |
| R-23 | Pane collapse missing — operator feedback (Round 5, 2026-05-22): the SpaarkeAi three-pane shell had no operator-facing way to collapse a pane to a narrow strip. The existing `ThreePaneLayout` already supported left/right collapse internally (via sessionStorage `*-visibility` keys), but the toggle UI was buried — only the collapsed strip itself was clickable, so there was no way to GET TO the collapsed state without code. Operator wanted SmartToDo's Kanban-style click-the-header-to-collapse pattern for all three panes (Assistant + Workspace + Context), with persistence across browser sessions. | **RESOLVED (2026-05-22 / task 094)** | Low | **Resolved (header-click collapse + 3rd-pane collapse + localStorage persistence)**: extended shared `PaneHeader` with optional `onCollapse` + `expanded` props that make the header act as a click/keyboard collapse trigger (with `role="button"`, `aria-expanded`, Enter/Space handlers, and a `stopPropagation` guard on the rightSlot wrapper so interactive children — History icon, WorkspacePaneMenu — don't bubble clicks up to the header). Extended `ThreePaneLayout` with externally-controlled collapse state for all three panes (added `centerCollapsed` + matching toggle for the center pane that the base layout didn't previously support; collapsed center renders as a 36px strip with the same rotated-label treatment as left/right). New SpaarkeAi-local `usePaneCollapse` hook owns the Set-based collapse state + localStorage persistence (`spaarke:panes:collapsed` JSON array) with try/catch-wrapped accessors per task 092's posture. New `PaneCollapseContext` exposes the toggle handle to each pane so they can wire `onCollapse` on their own `<PaneHeader>` without prop drilling. ConversationPane / WorkspacePane / ContextPaneController each now declare `onCollapse={() => toggle('<paneId>')}` and `expanded={!isCollapsed('<paneId>')}`. Reuse: visual pattern lifted verbatim from `SmartToDo.tsx` (Set-based `collapsedColumns` state) + `KanbanBoard.tsx` (columnCollapsed strip with `writingMode: 'vertical-rl'` + `transform: rotate(180deg)`). ADR-021 (Fluent v9 tokens — no hex/rgba), ADR-022 (React 19), ADR-028 (no auth surface change) all complied with. FR-25 preserved (LegalWorkspace standalone behaviorally unchanged; +40 bytes of unused conditional branch code only). Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Task 094 (pane collapse/expand)". |
| R-24 | Context pane blank after modal close + missing Semantic Search Criteria surface — operator feedback (Round 5, 2026-05-22): (a) clicking any GetStartedCards wizard card (e.g. Create Project, Find Similar, Assign Work) launched the wizard popup correctly, but when the operator closed the popup the Context pane went blank instead of returning to the GetStartedCards grid; root cause was the welcome-stage check in `renderContent()` short-circuiting on `currentStage === "welcome"` while ShellStage had already advanced (or never matched) so the fall-through hit `renderStageDefaultContent()` empty state; (b) Semantic Search Criteria had no surface in the SpaarkeAi shell — the full SemanticSearch Code Page was reachable only from outside the shell (sitemap / command bar), with no in-pane criteria editor to seed a search from inside SpaarkeAi. | **RESOLVED (2026-05-22 / task 095)** | Low | **Resolved (Context tool selector + Semantic Search Criteria + persist through modal close)**: added a Fluent v9 `<Menu>` dropdown in the Context pane's PaneHeader rightSlot (mirroring `WorkspacePaneMenu`'s pattern verbatim — `MenuTrigger` Button + `MenuPopover` + `MenuList` + `MenuGroupHeader` + MenuItems with active-checkmark) exposing two tools: **Quick Start** (default — existing `GetStartedCardsWidget`) and **Semantic Search** (new `SemanticSearchCriteriaTool` with in-pane Domain `<Dropdown>` + AI query `<Textarea>` + optional from/to date inputs + primary Search button). Search button launches `sprk_semanticsearch` via `Xrm.Navigation.navigateTo` (frame-walk Xrm resolver from `wizardLaunchers.ts`) with `query` + `domain` + `dateFrom` + `dateTo` URL params, 80% × 80% modal. Tool selection persisted in `localStorage["spaarke:context:selected-tool"]` (same try/catch-wrapped posture as task 094's `spaarke:panes:collapsed` — readPersistedTool validates against `VALID_CONTEXT_TOOL_IDS`); criteria persisted separately in `localStorage["spaarke:context:semantic-search-criteria"]`. The persisted `selectedTool` is the SOURCE OF TRUTH in `renderContent()` (overrides ShellStage when `semantic-search`; defaults to GetStartedCardsWidget on welcome stage AND on non-welcome stages with no server-driven `activeWidget`) — same fix benefits BOTH the new Semantic Search modal AND the existing playbook-modal wizard flow: any modal close finds the pane re-renders with the chosen tool still selected. Server-driven `context_update` events still resolve widgets when `activeWidget !== null` (Stage 3 Findings / Stage 4 Related Items flows preserved). FR-25 preserved — no LegalWorkspace files touched, LegalWorkspace bundle 569.35 KB gzip = byte-identical to task 094 baseline. SemanticSearch Code Page untouched. SpaarkeAi 896.59 KB gzip (+1.55 KB vs task 094). ADR-012 (SpaarkeAi-local placement correct), ADR-021 (Fluent v9 tokens — `colorBrandForeground1`, `colorNeutralForeground1/2/3`, `colorNeutralBackground1`, `colorNeutralStroke2`, `spacingHorizontalS/M`, `spacingVerticalXXS/XS/S/M`, `borderRadiusMedium`; no hex / rgba), ADR-022 (React 19 functional), ADR-025 (icons from `@fluentui/react-icons` v9: `ChevronDownRegular` / `AppsListRegular` / `SearchRegular` / `CheckmarkRegular`), ADR-028 (no auth surface change — criteria are client-side until modal hand-off; `sprk_semanticsearch` has its own MSAL bootstrap). Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Task 095 (Context tool selector + Semantic Search Criteria)". |
| R-18 | Workspace selection no render path; pane coordination signals unwired — operator smoke (2026-05-21, round 4) confirmed that selecting a layout from the SpaarkeAi `WorkspacePaneMenu` "Switch Workspace" dropdown silently did nothing in the deployed UI. Root cause: `handleLayoutSelect` only persisted a `sessionStorage` key + dispatched a legacy `window` CustomEvent (`spaarke:workspace-layout-changed`) but no SpaarkeAi component subscribed to either signal — so the workspace pane never updated to render the chosen workspace. Secondary issue: even if a render path existed, the operator's architecture (workspace pane has tabs, each tab is a widget, active tab = active context for Assistant + Context panes) had NO signal infrastructure for "the active workspace context just changed". Prior investigation (round 4) had identified that copying LegalWorkspace's 5 section factories into SpaarkeAi would drag in ~30 files / ~10K LOC with `DataverseService` runtime state, `FeedTodoSyncContext`, and the `@hello-pangea/dnd` peer dep — too tangled for a clean copy. Operator principle taken to its logical conclusion: don't copy factories — reuse the WHOLE working `LegalWorkspaceApp` as a single workspace widget. | **RESOLVED (2026-05-21 / task 087, Round 4 Fix 4 Option A)** | High | **Resolved (Option A — Embed)**: (1) added `initialWorkspaceId?: string` + `embedded?: boolean` props to `LegalWorkspaceApp.tsx`; in embedded mode the component suppresses its internal `<PageHeader>` (which carries its own workspace dropdown), footer, outer `<FluentProvider>`, and cross-device theme-sync side effects so it composes cleanly inside a SpaarkeAi tab. (2) added `src/solutions/LegalWorkspace/src/index.ts` barrel exporting `LegalWorkspaceApp` + `ILegalWorkspaceAppProps`. (3) added `@spaarke/legal-workspace` Vite alias + transpilation include in `src/solutions/SpaarkeAi/vite.config.ts` and added `@hello-pangea/dnd ^18.0.1` to SpaarkeAi's deps (required by LegalWorkspace's SmartToDo/KanbanBoard). (4) created `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorkspaceLayoutWidget.tsx` — a `WorkspaceWidgetComponent<{ layoutId, layoutName }>` that resolves Xrm via the frame-walk pattern and renders `<LegalWorkspaceApp initialWorkspaceId={data.layoutId} embedded />`; registered widget type `'workspace'` with `allowMultiple: true` so multiple workspace tabs can coexist (FIFO eviction still applies via MAX_WORKSPACE_TABS). (5) `WorkspacePaneMenu.handleLayoutSelect` now dispatches `widget_load` on the workspace channel carrying `{ widgetType: 'workspace', widgetData: { layoutId, layoutName }, displayName: layoutName }` in addition to the legacy applyActiveLayout signal — the pane subscriber's existing `widget_load` handler opens the tab via the standard pipeline. (6) Added a new optional `displayName?: string` field on `WorkspacePaneEvent` so per-instance tab labels (e.g. "Corporate Workspace") override the generic registry label "Workspace"; `WorkspacePane.tsx`'s widget_load handler now honours this override. (7) Signal infrastructure for future pane coordination: new `'active_widget_changed'` event type on the workspace channel; new `ActiveTabSnapshot` interface + `onActiveTabChange` option in `WorkspaceTabManagerOptions`; emit sites at `addTab` / `setActiveTab` (delta-guarded) / `closeTab` (when active changes) / `clearAllTabs` (when active changes); `WorkspacePane.tsx` wires the manager callback through a forwarding ref into `dispatch("workspace", { type: "active_widget_changed", widgetType, widgetData, tabId, displayName })`. NO consumers are wired in this task — Assistant + Context pane scoping is deferred to a follow-up. Bundle delta: SpaarkeAi 801.85 → 893.91 KB gzip (+92 KB, +11.5%) — much smaller than the ~570 KB worst case because SpaarkeAi already bundles @fluentui, lexical, react-window, @spaarke/ui-components/auth, and WorkspaceShell; only the 5 LegalWorkspace section factories + DataverseService + FeedTodoSyncContext + @hello-pangea/dnd are net new. LegalWorkspace bundle 569.29 KB gzip — byte-identical to baseline (FR-25 / NFR-10 satisfied) because the standalone runtime entry is `main.tsx → App.tsx` and never imports `LegalWorkspaceApp.tsx`. ADR-012 (the section factory hoist debate is sidestepped — embedded reuse instead), ADR-021 (Fluent v9 tokens only), ADR-022 (React 19 functional), ADR-028 (LegalWorkspace's existing auth surface preserved) all complied with. Deployed to spaarkedev1. See deploy memo "Supplemental Deploy — Round 4 Fix 4". |

---

## 9. Next Steps

1. **Review this plan.md** — confirm phase structure and resource discovery match expectations
2. **Run** `task-create` (called by /project-pipeline Step 3) to generate `tasks/{001..090}-*.poml` task files + `TASK-INDEX.md`
3. **Commit + push** project artifacts on current `work/spaarke-ai-platform-unification-r3` branch; open draft PR
4. **Begin** Phase 0 (task 001 — FR-07 backend spike) via `task-execute` skill

---

**Status**: Ready for Tasks
**Next Action**: Generate task files via `task-create` skill

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. The Discovered Resources section in §2 is the canonical list of ADRs / skills / patterns / guides that task files MUST reference in their `<knowledge>` blocks.*
