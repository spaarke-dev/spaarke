# Daily Briefing — SpaarkeAi Pattern D Migration (R2)

> **Last Updated**: 2026-06-18
>
> **Status**: In Progress

## Overview

R2 migrates the Daily Briefing widget to the canonical SpaarkeAi Pattern D dual-use model: one shared component lives in a new `@spaarke/daily-briefing-components` package, consumed by BOTH the standalone code page (`sprk_dailyupdate`) and the SpaarkeAi workspace pane. It also fixes the SpaarkeAi empty-state regression, the BFF `/narrate` dead-hyperlink defect, the "Multiple X" aggregated-bullet visibility gap, and adds native MDA-bell deep-links — plus three high-proximity file de-duplications. Producer layer (7 playbooks + scheduler + executor) is verified healthy and out of scope.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | 6-workstream WBS, dependency graph, parallel groups |
| [Design Spec](./spec.md) | AI-optimized requirements (21 FRs, 7 NFRs, 14 SCs) |
| [Design Doc](./design.md) | Human-friendly problem statement + solution approach |
| [Shared-lib Hygiene Proposal](./shared-lib-hygiene-proposal.md) | Deferred 10 lower-proximity findings (follow-on project) |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker with dependencies + parallel groups |
| [Current Task](./current-task.md) | Active task state (context recovery) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Planning → Implementation handoff |
| **Progress** | 0% (pre-implementation) |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | Spaarke architecture / ralph.schroeder@spaarke.com |
| **Branch** | `work/spaarke-daily-update-service-r2` |

## Problem Statement

Four verified defects in the consumer + prompt-builder layer of the Daily Briefing pipeline:

- **D1 — Orphan loader (SpaarkeAi)**: `loadSpaarkeAiNotificationContext` exists but has no injection seam into the LegalWorkspace registration shim. SpaarkeAi workspace pane renders empty state for users who clearly have unread notifications.
- **D2 — Two divergent component trees (Pattern D violation)**: Standalone code page and SpaarkeAi widget host two separate copies of the Daily Briefing UI. Bug fixes ship to one and not the other; new features drift apart. Calendar and Smart To Do already demonstrate Pattern D dual-use — Daily Briefing has not yet adopted it.
- **D3 — BFF `/narrate` dead hyperlinks**: The channel-prompt builder omits `regardingId`, so the LLM hallucinates `primaryEntityId` values. Approximately every aggregated bullet links to a nonexistent record.
- **D4 — Hidden action items**: AI-aggregated "Multiple X" bullets render a single narrative line. The N underlying notifications are not surfaced, so users cannot link to, To-Do, or dismiss specific items.

Plus three high-proximity duplications: `MicrosoftToDoIcon.tsx` × 3 copies, `authInit.ts` × 3 copies, `runtimeConfig.ts` × 3 copies. Each was added per-solution rather than hoisted.

## Solution Summary

Six coordinated workstreams. **P1** restores the SpaarkeAi seam via a factory option (`loadNotificationContext`). **P2** hoists the entire Daily Briefing UI into a new `@spaarke/daily-briefing-components` package and decomposes `useNotificationData` into three independent hooks (`useBriefingNotifications`, `useBriefingPreferences`, `useBriefingActions`). **P2a** adds an always-visible per-item sub-list beneath aggregated bullets, each row with its own entity link + Add-to-To-Do + Dismiss. **P2b** fixes the prompt to emit `regardingId` and adds server-side validation that nulls out hallucinated `primaryEntityId` values. **P3** populates `appnotification.data.actions[]` so MDA native bell renders "Open" buttons (only when `toasttype` is visible). **DD** hoists the three duplicated files into `@spaarke/ui-components` and `@spaarke/auth`.

## Graduation Criteria

The project is **complete** when (per spec.md §Success Criteria):

- [ ] **SC1**: SpaarkeAi workspace pane renders Daily Briefing with real notifications (verified as `ralph.schroeder@spaarke.com` in spaarkedev1 with N>0 unread)
- [ ] **SC2**: Same component file renders in both hosts (standalone + SpaarkeAi); grep confirms one source
- [ ] **SC3**: No dead hyperlinks across 10 random clicks (10/10 open the correct record)
- [ ] **SC4**: Every "Multiple X" bullet has a visible per-item sub-list with per-item link + To-Do + Dismiss
- [ ] **SC5**: Add-to-To-Do creates concrete `sprk_todo` records (field-level fidelity verified in Dataverse)
- [ ] **SC6**: BFF `/narrate` prompt emits `regardingId` per item; server-side validation nulls hallucinated `primaryEntityId`
- [ ] **SC7**: MDA native bell shows clickable "Open" buttons for visible-`toasttype` notifications
- [ ] **SC8**: Standalone Daily Briefing code page behavior unchanged for end users (per relaxed NFR-02)
- [ ] **SC9**: Zero `MicrosoftToDoIcon.tsx` / `authInit.ts` / `runtimeConfig.ts` files under `src/solutions/`
- [ ] **SC10**: `useNotificationData` split into three independent hooks; old monolithic hook deleted
- [ ] **SC11**: `@spaarke/daily-briefing-components` builds; subpath exports (`./components`, `./hooks`, `./services`, `./types`) resolve
- [ ] **SC12**: Dark mode unaffected across hoisted components (Fluent v9 semantic tokens; manual toggle test passes)
- [ ] **SC13**: BFF publish-size delta ≤ +1 MB compressed vs. ~45.65 MB baseline (per §10 NFR-01)
- [ ] **SC14**: Architecture docs updated (`SPAARKEAI-COMPONENT-MODEL.md` + `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` + `BUILD-A-NEW-WORKSPACE-WIDGET.md`)

## Scope

### In Scope

- P1 — Wiring seam for `loadNotificationContext` injection (SpaarkeAi → registration shim)
- P2 — Pattern D hoist into `@spaarke/daily-briefing-components`; `useNotificationData` split into 3 hooks
- P2a — Hybrid aggregation UX (narrative + per-item sub-list with link/To-Do/Dismiss)
- P2b — BFF `/narrate` prompt + server-side `primaryEntityId` validation
- P3 — `appnotification.data.actions[]` population in `CreateNotificationNodeExecutor` (visible-toast only)
- DD — Hoist `MicrosoftToDoIcon` to `@spaarke/ui-components`; consolidate `authInit` + `runtimeConfig` into `@spaarke/auth`
- Test coverage for the new shared package (unit + smoke)
- Architecture doc updates (`SPAARKEAI-COMPONENT-MODEL.md`, `SPAARKEAI-WORKSPACE-ARCHITECTURE.md`, `BUILD-A-NEW-WORKSPACE-WIDGET.md`)

### Out of Scope

- Producer-layer changes (7 playbooks, scheduler, executor) beyond P3 — verified healthy
- New notification playbook categories (similar-documents, similar-matters, budget alerts)
- Retiring the standalone Daily Briefing code page (explicitly retained per owner)
- The 10 lower-proximity duplications surfaced in the audit — captured in `shared-lib-hygiene-proposal.md` for a separate `spaarke-shared-lib-hygiene-r1` project
- Real-time SignalR push for instant notification delivery
- Mobile / responsive layout
- Changes to `sprk_userpreference` schema
- Auto-popup behavior (`useDailyDigestAutoPopup` unchanged)
- `sprk_playbooktype` OptionSet cleanup (owner resolved out-of-band 2026-06-18)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| New package `@spaarke/daily-briefing-components` (NOT extend `@spaarke/ui-components`) | Calendar + Smart Todo precedent; Spaarke convention is one package per dual-use widget | ADR-012 |
| Split `useNotificationData` into 3 independent hooks | Single Responsibility; independent cache lifetimes; re-render isolation | — |
| Server-side validation of AI-returned `primaryEntityId` | Defense in depth; prevents dead links even if prompt is regression-prone | ADR-013 |
| Per-item sub-row links use supplied `regardingId` (no AI involvement) | Deterministic; fixes dead-link AND vague-To-Do problems in one move | — |
| Preserve standalone code page (not retired) | Owner decision; surface used in some flows | ADR-006 |
| Pattern D dual-use shape | Calendar precedent codified; documented in `BUILD-A-NEW-WORKSPACE-WIDGET.md` | ADR-012 |
| Effect-based cross-hook coordination at consumer layer (Option A) | Idiomatic React; explicit; traceable; no hidden coupling | — |
| Relaxed FR-25/NFR-10 byte-stability (was strict in R1) | Avoid blocking shared-component improvements on standalone parity | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Hoist regresses standalone code page rendering | Medium | Low | Smoke test mounts `DailyBriefingApp` with mocked Xrm; manual visual comparison pre/post hoist; relaxed FR-25 prevents over-rotating on pixel parity |
| Three-hook split changes cache invalidation semantics | Medium | Medium | Effect-based coordination at consumer (Option A); explicit refetch on preferences change; document pattern in PR |
| MDA bell `data.actions[]` schema mismatch | Low | Low | Spec assumption FR-18 documents the expected shape; verify on first deployed notification in spaarkedev1 |
| BFF publish-size delta exceeds +1 MB | Low | Low | Pure-code P2b/P3 changes; no new NuGet deps expected; baseline captured pre-implementation; verify post-build per §10 NFR-01 |
| DD hoist of `authInit` breaks auth init order in one solution | High | Low | `createCodePageAuthInitializer` factory preserves the exact call sequence; each solution migrated independently; build green between each |
| AI returns hallucinated `primaryEntityId` despite prompt fix | Medium | Medium | Server-side validation (FR-17) nulls invalid IDs and logs warning; defense in depth |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Producer layer (7 playbooks + scheduler) | Internal | Ready | Verified healthy 2026-06-18 |
| BFF `/narrate` endpoint | Internal | Ready | Operational; receives P2b modifications |
| `@spaarke/ui-components` build pipeline | Internal | Ready | Receives hoisted `MicrosoftToDoIcon` |
| `@spaarke/auth` package | Internal | Ready | Receives new `createCodePageAuthInitializer` factory |
| Azure OpenAI service | External | Production | Existing `/narrate` dependency |
| Microsoft Graph / Dataverse Web API | External | Production | `Xrm.WebApi` for sub-row To-Do creation |
| spaarkedev1 environment access | External | Ready | Required for E2E verification (SC1, SC3, SC7) |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Ralph Schroeder | Overall accountability; spec/design author |
| Implementation | Claude Code | Task execution per POML files |
| Reviewer | TBD | Code review, design review |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-18 | 1.0 | Project initialized via `/project-pipeline` | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
