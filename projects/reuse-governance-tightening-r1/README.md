# Reuse Governance Tightening R1

> **Last Updated**: 2026-11-11
>
> **Status**: In Progress

## Overview

This project tightens how Spaarke finds and reuses existing capabilities. It adds a hand-curated capability index, one reuse scan per project at `/design-to-spec`, a weekly observation-only duplication sweep, two adversarial-reviewer checks, and owner decisions on the 9 grandfathered `@spaarke/*` packages. It also ships four small quality-tooling items (Learn MCP, knowledge-refresh watchdog, re-armed `CS0618`, a C# LSP trial).

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Phases, WBS, risks |
| [Design](./design.md) | Owner-approved design (all OQs decided; ROI-trimmed) |
| [Spec](./spec.md) | AI implementation specification (FR-01..FR-13, NFR-01..NFR-10) |
| [Tasks](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Project CLAUDE.md](./CLAUDE.md) | Operating manual for sessions in this project |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | see `tasks/TASK-INDEX.md` |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | spaarke-dev |

## Problem Statement

The reuse gate (`task-create` 3.5.6 `<justification>`, re-checked by `code-review` 6.6) works, but it finds prior art only by name (G1), never checks a project's plan for duplication (G2), does not observe duplication after merge (G3), and leaves the 9 grandfathered shared packages without a decision path (G4). The live risk is over-promotion (15 shared packages), not under-reuse.

## Solution Summary

Close each gap with the lightest mechanism that works: a ~30-row concept → component index cited by the existing gate; one `code-mapper` scan per project, gated by a dry-run; a weekly `jscpd` sweep at exit 0 that raises one quality alert for new cross-area clusters; F2/F4 checks in the adversarial reviewer; and a decision record per grandfathered package, executing at most three. No blocking duplication gate, no new ArchTests or baselines, no recurring agent cost.

## Graduation Criteria

The project is considered **complete** when:

- [ ] `.claude/patterns/CAPABILITY-INDEX.md` exists (~30 verified rows) and `Test-InstructionPaths.ps1` reports no new missing path for it
- [ ] `task-create` 3.5.6, `code-review` 6.6 and root `CLAUDE.md` §11 cite the index
- [ ] The reuse-scan dry-run result and go/no-go decision are recorded; if go, `/design-to-spec` and `project-pipeline` carry the scan
- [ ] The weekly sweep runs at exit 0 and opens/closes exactly one `weekly/duplication-sweep` alert
- [ ] `adversarial-reviewer.md` carries the F2 capability-duplication and F4 requirements-trace checks
- [ ] All 9 grandfathered packages have an owner-approved decision; three actions executed, six filed with findings; `SharedPackageCensusTests` green
- [ ] Learn MCP, knowledge-refresh watchdog and `CS0618` re-arm landed as separate PRs; a clean Release build of `Spaarke.sln` passes
- [ ] The C# LSP trial result and owner decision are recorded

## Scope

### In Scope

- Item 1: capability index and its wiring
- Item 2: reuse-scan dry-run, then (if go) the scan in `/design-to-spec` with a `project-pipeline` fallback
- Item 3: weekly `jscpd` sweep in `nightly-health.yml` + capability-duplication reviewer check
- Item 4: decisions on 9 grandfathered packages; execute `ai-context`, `legal-workspace`, `document-operations`
- Item 5: (a) Learn MCP, (c) knowledge-refresh watchdog, (d) requirements trace, (e) re-arm `CS0618`, (f) C# LSP trial

### Out of Scope

- 5(b) TypeScript strictness ratchet (deferred until after item 4)
- Any blocking duplication gate and everything in design.md §10 / §10.7
- Executing the other six package actions (filed as their own projects)
- Repo-wide type-aware ESLint; coding-agent eval set
- New BFF surface or Dataverse columns

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Agent finds, orchestrator judges and writes `reuse-scan.md` | `code-mapper` is read-only and fact-only | — |
| Sweep observes (exit 0), never gates | The LOC ratchet was retired for gating on a count proxy | — |
| Sanctioning a package = ADR-012 amendment | ADR-012 prescribes it (Path B) | [ADR-012](../../.claude/adr/ADR-012-shared-components.md) |
| Re-arm `CS0618` instead of a superseded-API process | The compiler fails new callers under `TreatWarningsAsErrors` | — |

Full list: `design.md` §11 and `notes/decisions.md`.

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Reuse scan at `low` effort misses duplicates | Med | Med | Dry-run is go/no-go; owner sees measured cost before any raise |
| Package consolidation breaks SpaarkeAi / SemanticSearch / Compose builds | High | Med | Builds + tests per consumer; DEV deploy with owner OK; `conflict-check` |
| Re-arming `CS0618` surfaces many framework obsoletions | Med | Med | < 1 h each fixed, rest filed with narrow `DiagnosticId` suppression (OQ-15) |
| Collisions on hot-path skills / ADR-012 with parallel projects | Med | High | Overlap warnings; PR #935 (r4) touches ADR-012 + census test |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| PR #2656 (ADR-012 census + `SharedPackageCensusTests`) | Internal | Ready | Merged |
| Microsoft Learn MCP endpoint | External | Ready | Confirm endpoint at task time |
| `jscpd` (npm, pinned) | External | Ready | via `npx` in CI |
| C# LSP plugin + `csharp-ls` | External | Unknown | 5(f) trial only |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | spaarke-dev | Decisions, approvals |
| Developer | Claude Code | Implementation |
| Reviewer | adversarial-reviewer (fable) + code-review | Review |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-11-11 | 1.0 | Initialized by `/project-pipeline` | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
