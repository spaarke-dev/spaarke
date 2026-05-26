# Spaarke AI Platform Unification R3

> **Last Updated**: 2026-05-26
>
> **Status**: ✅ Complete

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
| **Phase** | ✅ Complete |
| **Progress** | 100% (140 tasks shipped across 13 rounds of operator polish) |
| **Target Date** | TBD |
| **Completed Date** | 2026-05-26 |
| **Owner** | spaarke-dev |
| **Final commit (master)** | `f5015c2a` |
| **Follow-on project** | [`spaarke-ai-platform-unification-r4`](../spaarke-ai-platform-unification-r4/) |

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

- [x] **FR-01 Pane header parity**: ✅ Shipped via Task 010 (`<PaneHeader>` shared component) + Tasks 021/030/040 (pane integrations).
- [x] **FR-02..FR-09 Assistant pane**: ✅ Shipped via Tasks 020-026 + polish rounds 091-099.
- [x] **FR-07 Multi-file attach end-to-end**: ✅ Shipped via Task 001 (spike) + 024 (hook) + 050 (BFF) + 026 (wire) + 051 (tests).
- [x] **FR-10..FR-16 Workspace pane**: ✅ Shipped via Tasks 030-035 + polish rounds. WorkspacePaneMenu, FIFO eviction, templateFilter, Daily Briefing all working.
- [x] **FR-17..FR-22 Context pane**: ✅ Shipped via Tasks 040-045.
- [x] **FR-23 Auth**: ✅ Verified clean — `notes/audit-auth-2026-05-20.md` (Task 060) confirms zero token snapshots across 46 R3 files.
- [x] **FR-24 Error telemetry**: ✅ Shipped via Task 013 + 035 + helpers integrated into hooks across rounds.
- [x] **FR-25 / NFR-10 Backwards compatibility**: ✅ Verified during R3 — Task 063 confirmed standalone LegalWorkspace identical. *Note: 2026-05-25 decision to retire standalone LW code page makes this requirement moot going forward; tracked in R4 W-6.*
- [x] **NFR-01..NFR-03 Performance**: ✅ Verified — Task 074 confirmed pane render <500ms, History overlay <200ms + populate <300ms p95.
- [x] **NFR-06 Dark mode**: ✅ Verified — `notes/dark-mode-audit.md` (Task 062) confirms zero hex/rgba literals in new code.
- [x] **NFR-09 Persistence**: ✅ Task 065 shipped (`SessionPersistence` tab-list extension). *Note: operator feedback during R4 scoping suggests behavior shifted post-Task-065; tabs ARE persisting but browser-reopen lands on last tab not Home/default. R4 Phase 3 verifies + addresses the actual operator-visible issue.*
- [x] **NFR-12 Bundle size**: ⚠️ Deviation accepted via Task 061. Final R3 bundle ~918 KB gzip (vs ~508 KB ceiling). Root cause: `vite-plugin-singlefile` inlines lazy-imports. Phase G smoke (Task 074) confirmed NFR-01/NFR-03 still met. *R4 D-2 amends ADR-026 with "heavy library handling" subsection.*
- [x] **Deployment**: ✅ Production deployed — `sprk_spaarkeai` web resource updated continuously throughout R3, final state at master `f5015c2a`.
- [x] **Wrap-up**: ✅ This README updated; `notes/lessons-learned.md` written; `notes/bff-placement-justification-retroactive.md` (R4 F-1) written; plan.md status updated; ready for archive.

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
| 2026-05-26 | 1.1 | ✅ R3 marked Complete. 140 tasks shipped across 13 rounds. Lessons-learned recorded. Graduation criteria checked with notes for deviations (NFR-12, NFR-09). Follow-on R4 project formalized. | spaarke-dev (Phase 0 / E-1) |

---

*Generated by [/project-pipeline](/.claude/skills/project-pipeline/SKILL.md) on 2026-05-20 from [spec.md](./spec.md).*
*Wrap-up completed 2026-05-26 as Phase 0 of [spaarke-ai-platform-unification-r4](../spaarke-ai-platform-unification-r4/).*
