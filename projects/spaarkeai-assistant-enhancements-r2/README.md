# SpaarkeAI Assistant Enhancements R2

> **Portfolio**: [Project Issue #753](https://github.com/spaarke-dev/spaarke/issues/753) · Parent [Epic #421 (SPAARKE AI)](https://github.com/spaarke-dev/spaarke/issues/421) · Board [Project #2](https://github.com/users/spaarke-dev/projects/2)
>
> **Last Updated**: 2026-08-10
>
> **Status**: ✅ Complete

## Overview

Makes the SpaarkeAI Assistant **surface-aware** (it sees and can act on the currently-focused Workspace tab, starting with email), **proactive** (offers ≤3 context-relevant follow-on chips on tab open), and gives it a **true history/resume** (reopening a session restores the whole workspace — chat + tabs + open document + saved redline — not just the chat text). Also removes the low-value Notifications banner. Nearly all of it is read/wiring/reliability work over existing machinery.

## Quick Links

| Document | Description |
|----------|-------------|
| [Implementation Plan](./plan.md) | Phased plan + WBS |
| [Design Spec](./spec.md) | AI-optimized specification (27 FRs / 6 NFRs) |
| [Design Doc](./design.md) | Source human design |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown, dependencies, parallel groups |
| [AI Context](./CLAUDE.md) | Load-first context for task execution |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | ✅ Complete |
| **Progress** | 100% (all 27 tasks + wrap-up ✅; E/A/B/D/C shipped + deployed) |
| **Target Date** | — |
| **Completed Date** | 2026-08-10 |
| **Owner** | spaarke-dev |

## Problem Statement

The Assistant is blind to what the user is looking at: "summarize this" resolves against a server-side `UpdatedAt` heuristic rather than the actually-focused tab; it can't state an open email's subject/sender/thread; it never proactively offers relevant next steps; and reopening a History session restores only chat text — losing the tabs, open document, and saved redline. A Notifications banner adds noise without value. R2 is the reliability/wiring pass that closes these gaps on top of R1's shipped dispatch spine + catalog.

## Solution Summary

Feed the real focused tab into each chat turn via a **focus-stamp** on the existing `onDecorateOutboundBody` seam (no `SprkChat` fork). Fire **one grounded suggestion turn per tab** (cached by `tabId`, never on switch-back) to surface ≤3 context-relevant chips through the reactive chip surface. Implement `getAgentVisibleState()` on the email widget + a lean `Email` visible-state variant so the Assistant can see email metadata, with full-body fetch on-demand via `eml-render`. Route History through the rich `/restore`+`/tabs` resume path (clearing/remounting the workspace first), make the first Cosmos transcript write awaited/confirmed, fix the 404-on-missing contract, add writable stored titles + rename/delete, and retain filed analyses indefinitely. Remove the spine-driven suggestion surface (banner + cards) while preserving the notification spine.

## Graduation Criteria

The project is **complete** when:

- [x] Asking "summarize this" with an email focused resolves to that email (focus-stamp in body; agent prompt "(active)" matches the focused tab)
- [x] Opening an email tab shows ≤3 relevant follow-on chips; switching away and back fires **no** additional LLM turn (suggestion cached per `tabId`)
- [x] The Assistant can state an open email's subject/sender/thread (Email visible-state variant emitted)
- [x] Reopening a History session restores chat **+ tabs + document + attachment chip + redline** (rich-path resume)
- [x] A session's first turn survives Redis eviction — reopen shows the transcript, not a blank pane (awaited `messages[0]` write)
- [x] History rows show descriptive titles + preview + tab summary; rename/delete work; the up-arrow is gone
- [x] "Set related record" prompts existing-vs-new, files the analysis on the matter's Analyses tab, and the filed session is resumable after >90 days
- [x] The Notifications banner is removed from the Assistant; the spine + Daily Briefing + Communications badge/toast still work (regression)
- [x] BFF publish size ≤ 60 MB compressed on every BFF-touching task (48.41–48.45 MB across the D/C deploys)

> **Verification status at close (2026-08-10)**: all criteria implemented, deployed to `spaarkedev1`, and smoke-verified. Owner E2E **cleared for A + B** (2026-08-06). C + D shipped + smoke-verified with owner E2E exercised across UAT rounds 1–2 (email visibility + history restore fixes verified). The **auth cold-start re-UAT** (post `requireSilentOnly` revert, deployed 2026-08-10) is the one owner check completing at close — the fix restores known-good pre-Aug-4 popup-fallback behavior. Project closed by owner direction.

## Scope

### In Scope

- **A** — Active-tab awareness (focus-stamp) feeding the focused tab into each chat turn
- **B** — Proactive follow-ons: one grounded suggestion turn per tab (cached), ≤3 chips
- **C** — Email Assistant-visibility: `getAgentVisibleState()` + `Email` visible-state variant + `email` context type
- **D** — History robustness & true resume: rich-path restore, transcript-write reliability, 404 contract, stored/rename/delete titles, attachment rehydrate, grouping/search, "Set related record", indefinite retention for filed analyses, Reanalyze chip
- **E** — Remove the Notifications banner (preserve the spine)

### Out of Scope

- Per-tab sessions / session fragmentation (rejected — one thread + focus-stamp)
- Full per-widget UI-state serialization ("full auto-resume") — durable-artifact resume only
- New dispatch pipeline / ranker (reuse R1's spine + catalog)
- New real-time push infrastructure (R1.5 Azure SignalR is a separate project)
- Removal of the notification **spine** — E removes only the Assistant **banner** surface
- Building/wiring the email widget (already real + registered post-r5)
- `EmailStubWidget` reconciliation — deferred to `email-communication-solution-r5` (FR-C5)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Active-tab content is visible to the agent (compact-ambient) | User's focus is implicit consent; bounded shape + background metadata-only | ADR-015 (Path A exception) |
| Proactive chips + title-gen are grounded turns, not classifiers | Same one grounded decider; no second intent mechanism | ADR-039 |
| Reliability/retention changes stay within Cosmos tiering | No new store; widen when/how-long, not where | ADR-040 |
| "Set related record" uses polymorphic `regarding` | Reuse existing field-set | ADR-024 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Tab-restore overwrite hazard (FR-D1) corrupts stored tabs on resume | High | Med | Clear/remount workspace FIRST; regression test the overwrite path; task 035 at opus/xhigh |
| Cosmos retention change (FR-D10) causes data loss | High | Low | Spike per-doc TTL feasibility first; idempotent cleanup only deletes past-due unfiled; task 033 opus/xhigh |
| Email visible-state can't reach widget data | Med | Med | Widget wrapper holds no email data — derive compact shape from `useEmailWorkspaceRecord` |
| ~~Merge collision with `spaarke-notification-spine-r1`~~ (retired 2026-08-05) | — | — | notification-spine-r1 + analysis-hub-r1 both fully merged to master; no live overlap. Normal `/conflict-check` hygiene only |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| email-communication-solution-r5 | Internal | ✅ Merged (2026-08-05) | Email widgets + `eml-render` available; C unblocked |
| R1 dispatch spine + catalog | Internal | ✅ Shipped | Reused by A/B/D |
| Cosmos / Redis / Dataverse / SPE | External | Ready | Existing infra; no new services |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-08-05 | 1.0 | Project initialized via /project-pipeline | spaarke-dev |
| 2026-08-10 | 2.0 | **Project complete.** E/A/B/D/C shipped + deployed; Phase 0 UAT quick-wins + auth cold-start fix (`requireSilentOnly` revert) deployed; test-diet clean (0 scaffolding, 0 orphans); wrap-up gates + docs finalized. | spaarke-dev |

---

*Based on Spaarke development lifecycle. Source: [design.md](design.md) → [spec.md](spec.md).*
