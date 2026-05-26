# Spaarke AI Platform Unification R4

> **Last Updated**: 2026-05-26
>
> **Status**: In Progress

## Overview

R4 consolidates the ~30 follow-up items that surfaced during R3 (Moment 1: Arrival) into a single post-shipping round. **34 IN items across 8 phases (~116h estimated effort, ~14-15 working days).** R4 also formalizes the **two-wrapper SpaarkeAi architecture** (Dashboard wrapper via `LegalWorkspaceApp` + Direct widget wrapper via `WorkspaceWidgetRegistry`), retires the standalone LegalWorkspace code page, and codifies BFF Hygiene §10 governance.

## Quick Links

| Document | Description |
|----------|-------------|
| [Spec](./spec.md) | AI-optimized specification (14 FRs / 9 NFRs / 7 DRs / 2 PRs) — source of truth |
| [Plan](./plan.md) | Implementation plan + WBS + discovered resources |
| [Backlog](./backlog.md) | Per-item analysis + 2026-05-25 IN/DEFER scoping decisions |
| [Tasks](./tasks/TASK-INDEX.md) | Task tracker (created by `task-create`) |
| [CLAUDE.md](./CLAUDE.md) | AI context for this project (load first) |
| [README.original.md](./README.original.md) | Operator-authored README (pre-pipeline) |
| [plan.original.md](./plan.original.md) | Operator-authored plan (pre-pipeline) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Implementation (planning artifacts complete; tasks pending) |
| **Progress** | 0% |
| **Target Date** | ~2026-06-15 (3-4 weeks sequential; 2-3 weeks parallelized) |
| **Completed Date** | — |
| **Owner** | spaarke-dev |

## Problem Statement

R3 shipped Moment 1: Arrival (140 tasks + 13 polish rounds), but left ~30 flagged follow-up items spanning build hygiene, documentation gaps, BFF governance, componentization audit findings, and architectural items that warranted their own round. Without R4, these items would compound — drift across rounds, regress build cleanliness, and leave the new SpaarkeAi two-wrapper architecture undocumented. R4 also surfaces a recently-discovered bug (`WorkspaceLayoutWizard` catalog drift — Calendar + Daily Briefing not pickable) and an operator-mandated change (raise chat attachment cap from 5 MB to 25 MB).

## Solution Summary

Eight-phase rollout: (1) R3 wrap-up + retroactive BFF governance memo; (2) documentation round establishing the authoritative two-wrapper model and decision criteria; (3) BFF facade audit; (4) UQ-03 verify-then-fix; (5) workspace builder fix + first end-to-end mount-source demos for Assistant→Workspace and Context→Workspace; (6) substantive code refactors (attachment cap raise, hook consolidation, `WorkspaceRenderer` interface, BFF DTO/endpoint hygiene); (7) build hygiene cluster (gitignore, tsc, ESLint v9, type-drift); (8) project closure. Heavy doc + small-code work first, substantive code in the middle, build hygiene clustered at the end. Per ADR-013 refined + CLAUDE.md §10, every BFF-touching task includes a Placement Justification + publish-size verification.

## Graduation Criteria

The project is considered **complete** when:

### Code + deploy
- [ ] All 34 IN items shipped to dev environment (`sprk_spaarkeai` updated; `sprk_corporateworkspace` deprecated per W-6)
- [ ] BFF deploys (A-4, B-4, B-5) verified for publish-size delta; no new HIGH-severity CVEs
- [ ] All new ADRs merged (ADR-025, ADR-026 + D-2 amendment)
- [ ] Build clean: 0 `tsc --noEmit` errors; 0 lint errors; 0 build warnings introduced by R4
- [ ] No tracked build artifacts in `git status` after fresh clone + build

### Documentation
- [ ] `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` published (W-1)
- [ ] `BUILD-A-NEW-WORKSPACE-WIDGET.md` rewritten with two-wrapper decision tree (W-2)
- [ ] `LEGALWORKSPACE-RETIREMENT.md` published (W-6)
- [ ] ADR-025 + ADR-026 published in both concise + full forms (A-2)
- [ ] ADR-026 amended with heavy library handling section (D-2)
- [ ] `DATA-ACCESS-DECISION-CRITERIA.md` published (C-1)
- [ ] `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` published (C-2)
- [ ] `CHAT-ATTACHMENT-POLICY.md` published (A-4)
- [ ] CLAUDE.md §10 updated with publish-size baseline rule (F-3)

### Behavior
- [ ] Workspace builder shows all 7 sections including Calendar + Daily Briefing (W-3)
- [ ] Tab persistence matches operator-confirmed expectation (A-5)
- [ ] Assistant → Workspace demo works end-to-end (W-4)
- [ ] Context → Workspace demo works end-to-end (W-5)
- [ ] Chat attachment upload works at 25 MB; rejects >25 MB with clear error (A-4)
- [ ] Workspace layout `modifiedOn` displayed in Manage pane (B-4)
- [ ] PATCH semantics work for layout updates with concurrency safety (B-5)
- [ ] CalendarSidePane and SpaarkeAi embedded Calendar visually + behaviorally identical (B-6)

### Process
- [ ] R3 marked Complete (E-1)
- [ ] R4 lessons-learned.md written; README → Complete
- [ ] `/repo-cleanup projects/spaarke-ai-platform-unification-r4` completed

## Scope

### In Scope

34 IN items per [spec.md](./spec.md) and [backlog.md](./backlog.md) §Scoping Decisions:
- **Phase 0** (~4h): E-1 R3 wrap-up · F-1 retroactive BFF memo
- **Phase 1** (~21h): W-1 W-2 A-2 C-1 C-2 D-2 F-3 — documentation round
- **Phase 2** (~2h): F-2 BFF facade audit
- **Phase 3** (~10h): A-5 UQ-03 verify+fix
- **Phase 4** (~19h): W-3 W-4 W-5 W-6 — workspace builder + mount sources
- **Phase 5** (~31h): A-4 C-3 C-4 B-4 B-5 B-6 — substantive code changes
- **Phase 6** (~21h): B-1 B-2 B-3 B-7 B-8 B-9 B-10 B-11 — build hygiene
- **Phase 7** (~2h): R4 wrap-up

### Out of Scope

- **A-1**: Stages 2-4 header treatment (Moment 2 scope)
- **A-3**: AI-vs-User visual + AIReasoningSurface (strategy work)
- **C-5**: Hoist remaining 5 LW-internal section factories (no forcing function)
- **C-6**: Hoist `runtimeConfig` to `@spaarke/auth` (works today)
- **C-7**: Section registry plug-in style (no 3rd-party need)
- **D-2 implementation**: Bundle-size Option 2 separate-web-resources (ADR amendment only is IN)
- **D-3**: Bundle-analyzer verification (only relevant after D-2 implementation)
- **D-1**: Merged into A-5
- **R3 FR-25 / NFR-10**: "Standalone LegalWorkspace continues to function identically" — superseded by W-6 (code page retired)

## Key Decisions

| Decision | Rationale | ADR / Source |
|----------|-----------|---|
| Two wrappers retained (Dashboard + Direct widget) | Serve distinct use cases (compose vs sophisticated single-purpose) | W-1 framing; OC-R4-06 |
| LegalWorkspace standalone code page retired; library retained | Replaced by SpaarkeAi; no operator demand for standalone | W-6; OC-R4-05 |
| A-4 raised to 25 MB (not just policy doc) | Operator mandate; chat attachments now first-class | OC-R4-01 |
| A-5 verify-first | R3 verification finding contradicted by user feedback | OC-R4-02 |
| D-2 ADR amendment only (no impl) | Forcing function absent; deferred indefinitely | OC-R4-04 |
| F-1 light memo, scoped to R3 | One-time retroactive close-out; rule applies prospectively | OC-R4-03 |

See [spec.md §Owner Clarifications](./spec.md#owner-clarifications) for the full decision register.

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| A-5 verification reveals different bug | Medium | High | Phase 3 verify-first design absorbs this; re-scope remediation after verify |
| A-4 25 MB cap blows bundle-size budget or BFF text-content limits | Medium | Medium | Bundle impact from text extraction (capped chars, not file binary); confirm in build verification |
| C-3 hook consolidation breaks LW or SpaarkeAi | High | Medium | Test both surfaces end-to-end after consolidation; keep auth-source flexible |
| W-6 LW retirement breaks unanticipated consumer (Dataverse form) | High | Low | Audit `corporateworkspace` references in Dataverse customizations before retiring |
| W-4/W-5 mount-source wiring exceeds estimate | Medium | Medium | Scope reduction available: ship dispatch + one viewer widget; broader coverage to R5 |

See [plan.md §Risk Register](./plan.md) and [plan.original.md §8 Risk Register](./plan.original.md) for full risk register (10 items, R-1..R-10).

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Dataverse dev environment (`spaarkedev1`) | Internal | Ready | Used in R3; no changes expected |
| Azure App Service (BFF deploy slots) | Internal | Ready | Production + warmup slots stable through R4 |
| Operator review | Internal | On-demand | Phase 3 (A-5) + Phase 4 (W-3/W-4/W-5 demos) gates |
| R3 master commit `3813af32` | Internal | Ready | R3 shipped 2026-05-22; current baseline |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | spaarke-dev | Overall accountability; scope decisions; verification gates |
| Developer | Claude Code (Opus) | Implementation; task execution via `task-execute` skill |
| Reviewer | spaarke-dev + automated quality gates (code-review + adr-check) | Code review; ADR compliance |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-05-25 | 0.1 | Initial scoping (operator-authored README → README.original.md) | spaarke-dev |
| 2026-05-26 | 0.2 | spec.md created from plan.md + backlog.md via `/design-to-spec` | spaarke-dev / Claude |
| 2026-05-26 | 1.0 | Project pipeline run; CLAUDE.md + current-task.md + tasks/ scaffolded | spaarke-dev / Claude |

---

*Template version: 1.0 | Based on Spaarke development lifecycle | Original operator README preserved at [README.original.md](./README.original.md)*
