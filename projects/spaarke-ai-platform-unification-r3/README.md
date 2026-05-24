# Spaarke AI Platform Unification R3

> **Last Updated**: 2026-05-20
>
> **Status**: In Progress

## Overview

This project reshapes the welcome state of the SpaarkeAi three-pane code page (Assistant / Workspace / Context) to align with the Spaarke UI/UX strategy's "Moment 1: Arrival" specification. It introduces a unified `<PaneHeader>` primitive across all three panes, embeds the existing standalone LegalWorkspace experience as the Workspace pane's default "Home" tab, and replaces the Context pane's playbook gallery with seven Get-Started action cards that route wizards into the Workspace pane.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with phase WBS and discovered resources |
| [Design Spec](./spec.md) | AI-optimized specification (25 FRs, 12 NFRs, 12 ADRs) |
| [Design Document](./design.md) | Original human design document for Moment 1: Arrival |
| [Project Context](./CLAUDE.md) | AI context file for this project |
| [Active Task State](./current-task.md) | Current task tracker (context recovery) |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker (created by task-create) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Implementation |
| **Progress** | 0% (planning artifacts complete; tasks pending generation) |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | spaarke-dev |

## Problem Statement

The SpaarkeAi code page launched in R2 with a three-pane shell (Assistant / Workspace / Context) and lifecycle stages (Welcome / Active / Review / Complete), but the welcome state has visual and structural friction that the Spaarke UI/UX strategy explicitly addresses in "Moment 1: Arrival":

- Pane headers are inconsistent across the three panes (different styles, fonts, spacing).
- Assistant pane welcome screen is cluttered (sparkle icon + heading + 4 prompt cards + "Open in Word" toolbar button); input is disabled on cold load until a session exists.
- Workspace pane shows a custom landing widget that duplicates LegalWorkspace functionality already built and shipping standalone.
- Context pane shows a playbook gallery on the welcome stage that doesn't represent the user's primary starting actions (create matter, create project, summarize, find similar, etc.).
- No "Daily Briefing" surface exists despite the AI backend supporting it (`/api/ai/daily-briefing/narrate`).

## Solution Summary

Build a unified `<PaneHeader>` primitive in `@spaarke/ui-components` and use it across all three panes. Trim Assistant chrome to the input + recent conversations only; activate the input on load; replace tabs with a History overlay button. Embed LegalWorkspace as the Workspace pane's default "Home" tab via a shared `WorkspaceShell`, add a `WorkspacePaneMenu` (workspace switcher + tabs dropdown) in the pane header right-slot, raise the configurable tab limit to 8, and surface Daily Briefing as a new LegalWorkspace section type. Replace the Context pane's `PlaybookGalleryWidget` with a new `GetStartedCardsWidget` (7 action cards in a 2-col scrollable grid) that routes wizards into the Workspace pane via the existing `PaneEventBus`. Backwards-compatible — the standalone LegalWorkspace code page continues to function identically.

## Graduation Criteria

The project is considered **complete** when:

- [ ] **FR-01 Pane header parity**: All 3 panes use the same `<PaneHeader>` component (code grep + visual diff at 1280px / 1600px / narrow).
- [ ] **FR-02..FR-09 Assistant pane**: Manual smoke confirms ChatRegular icon, "Assistant" title, no tab buttons, History overlay opens on click, welcome chrome trimmed (no sparkle, no heading, no 4 prompt cards, no "Open in Word"), input editable on cold load, multi-file attach (up to 5) with chip strip.
- [ ] **FR-07 Multi-file attach end-to-end**: User attaches 5 .txt files → backend receives `attachments` array of 5 items → AI reply demonstrates knowledge of all 5 file contents.
- [ ] **FR-10..FR-16 Workspace pane**: AppsListRegular icon visible; LegalWorkspace embedded as Home tab; WorkspacePaneMenu shows Open/Home/Switch Workspace/Edit sections with dividers; opening 9 widgets evicts oldest non-Home via FIFO; Layout Wizard launched from SpaarkeAi shows 6 templates (standalone shows 9); Daily Briefing renders content; empty state shows "Nothing to see right now — enjoy your day".
- [ ] **FR-17..FR-22 Context pane**: Shared PaneHeader visible; 7 GetStarted cards in 2-col scrollable grid; Create Matter opens `create-matter-wizard` as a new top-tab in Workspace; Assign Work invokes `sprk_createworkassignmentwizard` via `Xrm.Navigation.navigateTo` in Power Apps host; stage label is "Get Started" on welcome.
- [ ] **FR-23 Auth**: Zero `accessToken` props or state snapshots in new components; all fetches via `authenticatedFetch` per ADR-028.
- [ ] **FR-24 Error telemetry**: 429 from Daily Briefing, file extraction errors, and HistoryOverlay load failures all emit App Insights custom events with expected schema (no happy-path events).
- [ ] **FR-25 / NFR-10 Backwards compatibility**: Standalone LegalWorkspace still shows all 9 templates in its wizard; existing user layouts render identically.
- [ ] **NFR-01..NFR-03 Performance**: Pane render <500 ms; History overlay open <200 ms + populate <300 ms p95; session restore p95 <500 ms.
- [ ] **NFR-06 Dark mode**: No hex literals in new code (lint enforced); manual light/dark side-by-side passes.
- [ ] **NFR-09 Persistence**: Open 3 non-Home tabs + refresh → all 3 tabs restored.
- [ ] **NFR-12 Bundle size**: `npm run build:prod` of SpaarkeAi reports <250 KB gzip delta vs R2 baseline; PDF.js + mammoth absent from initial bundle.
- [ ] **Deployment**: Production SpaarkeAi code page deployed via `scripts/Deploy-SpaarkeAi.ps1` and smoke-tested via `ui-test` skill.
- [ ] **Wrap-up**: Lessons-learned recorded; README status set to Complete; project artifacts archived.

## Scope

### In Scope

- Unified `<PaneHeader>` shared component (icon + title + right-slot) in `@spaarke/ui-components`
- Assistant pane chrome reduction, input-on-load activation, multi-file attach (≤5), History overlay, toolbar restructure (per FR-02..FR-09)
- Workspace pane: embed LegalWorkspace as Home tab, WorkspacePaneMenu, MAX_WORKSPACE_TABS = 8 configurable, layout-template filter (6 of 9), Daily Briefing section + 429/empty state (per FR-10..FR-16)
- Context pane: shared PaneHeader, GetStartedCardsWidget (7 cards), wizard widget wrappers, AssignWorkWizardLauncher, stage label rename (per FR-17..FR-22)
- Cross-cutting: error-only telemetry, all BFF calls via `authenticatedFetch` (ADR-028), backwards compatibility with standalone LegalWorkspace
- Possible BFF endpoint extension: `POST /api/ai/chat/sessions/{sessionId}/messages` to accept `attachments[]` (conditional on FR-07 spike result — task 001)

### Out of Scope

- Stages 2–4 lifecycle UI (active chat, review, complete) — only welcome-stage changes here
- Full SharePoint Embedded file-upload integration (`+` button uploads to message context only, not Dataverse Document entity)
- New cross-cutting visual conventions from strategy §5.1 (AI-vs-User distinction, AI reasoning surface) — deferred to Phase A foundations work in a later project
- ADRs formalizing PaneEventBus pattern and stage lifecycle pattern — flagged as R2 lessons-learned candidates
- Strategy doc's Moments 2–4 components (TriageQueueWidget, EntityWorkspaceWidget, DocumentCanvas, etc.)
- Full App Insights happy-path telemetry — only error events are in-scope (per OC-09)
- Localization / i18n — English-only, hardcoded strings (per OC-10)

## Key Decisions

| Decision | Rationale | Source |
|----------|-----------|--------|
| Reuse existing LegalWorkspace via `WorkspaceShell` embed (not rebuild) | LegalWorkspace already ships with layouts, sections, and persistence — embed not duplicate | OC-04, ADR-012 |
| Side overlay for History (Claude-Code-style), not popover | Replaces conversation in place; more discoverable for users coming from Claude Code | OC-01 |
| Multi-file attach (up to 5) extracted client-side | Avoids new SPE Document entities; in-memory context only | OC-02, OC-07, FR-07 |
| `MAX_WORKSPACE_TABS = 8` configurable, Home exempt | Predictable FIFO eviction; configurable in one place | OC-03, FR-13 |
| Error-only telemetry in this project | Scope discipline; happy-path events deferred | OC-09, FR-24 |
| English-only, no i18n | Match R2; no FormatJS wiring | OC-10 |
| Stay on existing `work/spaarke-ai-platform-unification-r3` worktree branch (not new `feature/`) | Repo worktree convention; commit already in place | This project, deviation from project-pipeline default |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| R-1: Backend chat-message endpoint may not accept `attachments[]` (FR-07 requires extension) | Medium | Medium | Task 001 is a 30-min spike that verifies before committing to frontend payload shape; Phase E (backend tasks) unlocked only if needed |
| R-2: `Xrm.Navigation.navigateTo` unavailable in Vite dev | Low | High (in dev) | Feature-detect `window.Xrm`; show "Open in Spaarke" placeholder in non-host environments (per A-2, FR-20) |
| R-3: `ActionCard` from LegalWorkspace may not be context-agnostic | Low | Low | If lift to `@spaarke/ui-components` is needed, refactor before consuming in GetStartedCardsWidget (per A-3) |
| R-4: Daily Briefing endpoint hammered on tab switches (no per-user cache) | Low | Medium | Per-user TTL caching (~5 min) per ADR-014; verified via `useDailyBriefing` hook + UQ-02 |
| R-5: Non-Home tabs lost on refresh (R2 D-08 may not serialize tab list) | Medium | Low | NFR-09 verification step; if gap surfaces, extend `SessionPersistenceService` (per UQ-03) |
| R-6: Bundle size delta exceeds 250 KB gzip due to PDF.js + mammoth | Medium | Medium | Lazy-load both libs on first `+` click; verify via webpack-bundle-analyzer (NFR-12, UQ-04) |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R2 (`spaarke-ai-platform-unification-r2`) merged to master | Internal | Ready | commit `b40dc3e6` |
| Auth v2 (`spaarke-auth-v2-and-hardening`) merged to master | Internal | Ready | commit `e649f244` |
| `/api/ai/chat/sessions`, `/api/ai/daily-briefing/narrate`, `/api/workspace/layouts/default`, `/api/workspace/layouts` endpoints | Internal | Ready | Existing BFF endpoints |
| `WorkspaceShell`, `useWorkspaceLayouts`, `WorkspaceLayoutWizard`, `ActionCard`, `sectionRegistry` | Internal | Ready | LegalWorkspace + Spaarke.UI.Components |
| `PaneEventBus`, `AiSessionProvider`, `WorkspaceWidgetRegistry`, `ContextWidgetRegistry` | Internal | Ready | Spaarke.AI.Widgets |
| `pdfjs-dist`, `mammoth` (NPM) | External | Not installed | New deps for FR-07; lazy-loaded |
| `@fluentui/react-icons` (incl. `ChatRegular`, `AppsListRegular`, `DocumentRegular`, `HistoryRegular`, `AttachRegular`, `AddRegular`, `DismissRegular`) | External | Ready | Existing dep, no version bump |
| `Xrm.Navigation.navigateTo` (host-only) | External | Ready (host) | Feature-detect for Vite dev |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-05-20 | 1.0 | Initial draft — project initialized from spec via /project-pipeline | spaarke-dev |

---

*Generated by [/project-pipeline](/.claude/skills/project-pipeline/SKILL.md) on 2026-05-20 from [spec.md](./spec.md).*
