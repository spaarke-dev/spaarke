# Code Quality & Assurance R3

> **Portfolio**: register under Epic **#427** `[Epic]: Code Quality` (one Project Issue; surfaces = workstreams, no per-surface Issues). Populated at `/project-pipeline` time.
> **Status**: Initialized — assessment-first; execution operator-gated
> **Branch**: `work/code-quality-and-assurance-r3` (single worktree; surfaces = workstreams)

## Quick Links

- [plan.md](plan.md) — implementation plan + WBS
- [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) — task tracker + parallel groups
- [spec.md](spec.md) — AI implementation specification
- [design.md](design.md) — program design (umbrella)
- [../bff-api-cleanup-remediation-r1/design.md](../bff-api-cleanup-remediation-r1/design.md) — BFF surface workstream #1 (assessed)

## Overview

R3 is a **standing quality program** — not a one-off polish sprint. It re-baselines the codebase grade honestly against a fixed rubric (D1–D11), drives verified per-surface assessments to an **A+ senior-panel standard**, and hardens forcing-functions so the grade *holds* as the codebase keeps growing. It runs as a **single project in one worktree**, with each code surface (BFF, shared client libs, shared server libs, PCF, Dataverse model, code pages, plugins) executed as a **workstream/phase** in one `TASK-INDEX.md`.

Lineage: **r1** (quality *system* — tooling/scorecard, C→B) → **r2** (first *structural* remediation, 17 tasks ✅, B→A-) → **r3** (this program).

## Problem Statement

R1 built the quality *system*; R2 did the first *structural* remediation. The program then went dormant ~5 months while the codebase roughly doubled in active surface (the entire 2026-Q2/Q3 wave — AI redesign, Compose, Communication/Email, Notification Spine, Modal System, Teams App, external-access SPA, etc.; **30 active worktrees, 19 touching the BFF**). The result is **grade drift**: new code accumulated debt below the enforcement threshold, and at least one real correctness/security regression shipped (invoice financial-totals always-failing cast; an unauthenticated Dataverse-write endpoint). Reaching — and *holding* — an A+ grade now requires a coordinated, repeatable, multi-surface program.

## Proposed Solution

1. **Rubric + scorecard** — publish `docs/standards/CODE-QUALITY-RUBRIC.md` (D1–D11) and a living `notes/SCORECARD.md`; every surface scored on the same ruler.
2. **Repeatable assessment engine** — a reusable `quality-assessment` multi-agent Workflow: fan-out per rubric dimension → **mandatory Fable adversarial-verification pass** → prioritized remediation `design.md`. Assessments are read-only ⇒ conflict-free.
3. **Assessment-first** — run the full Fable-verified assessment across every surface FIRST (the gating deliverable), then build remediation plan/tasks from the verified findings.
4. **BFF remediation** (workstream #1, already assessed) — ~2.7k LOC dead-code deletion, 2 latent prod-bug fixes, 13→1 downcast consolidation, `@spaarke/auth` closure, facade compliance, folder migration, DI decomposition, repo hygiene.
5. **Forcing-functions** — expanded `Spaarke.ArchTests`, analyzers-as-errors, CI gates (CVE/size/doc-drift), **activated per-surface** (each surface flips its own gate as its last step; no repo-wide big-bang).
6. **Horizontal sweeps** — security (`@spaarke/auth` consistency), test quality (ADR-038 + `/test-diet`), dependency/CVE, observability, doc-drift.

## Scope

### In Scope

- Program scaffolding: rubric, scorecard, reusable `quality-assessment` Workflow, Phase-0 re-baseline.
- Full Fable-verified read-only assessment of all remaining surfaces (client libs, server libs, PCF, Dataverse model + ALM, code pages + build sprawl, plugins).
- BFF surface remediation (already assessed; 8 tasks across dead-code / bugs / downcast / auth / facade / migration / DI / hygiene).
- Forcing-functions authoring (ArchTests, mechanical baseline, CI gates) — per-surface activation.
- Horizontal sweeps (security, test quality, dependency/CVE, observability, doc-drift).
- Reconciliation of the archived 12-item March R3 draft; portfolio registration under Epic #427; file NG1 as an Idea.

### Out of Scope

- Two-Dataverse-stacks unification (ServiceClient vs raw-HTTP; needs its own ADR) — **filed as an Idea** (NG1).
- BFF↔microservice extraction (covered by the 2026-05-20 assessment; deferred).
- Merging the two live `.eml` builders; merging the two distinct R6 financial handlers; migrating `[Obsolete]` members with live callers; base classes for `Ai/Handlers`+`Ai/Nodes`.
- **Surfaces 2–6 remediation is deferred** until each surface's Fable-verified assessment produces a remediation design (then task-created into this same TASK-INDEX).
- Any new feature — the net code delta is behavior-preserving / removal-and-consolidation.

## Graduation Criteria

- [ ] `docs/standards/CODE-QUALITY-RUBRIC.md` published; every surface scored against D1–D11.
- [ ] Reusable `quality-assessment` Workflow runs end-to-end with a mandatory Fable verification stage.
- [ ] Honest re-baseline published in `notes/SCORECARD.md`; no unverified aggregate grade; March "95/100" superseded.
- [ ] Every remaining surface has a Fable-verified assessment `design.md` **before** its remediation is planned.
- [ ] BFF surface executed: 6 dead-code items removed (zero dangling refs), 2 bugs fixed (invoice-totals test passing), 13 downcasts → 1 extension, auth closed via `@spaarke/auth`, 4 facade violations behind `PublicContracts/`, `Endpoints/` deleted (route-dump identical), `CommunicationModule` decomposed, 2 tarballs untracked; publish ≤ 60 MB compressed with delta vs 46.89 MB reported.
- [ ] Forcing-functions live (per-surface activation): ArchTests expanded, analyzers-as-errors on, lint/CVE/size/doc-drift CI gates green.
- [ ] Horizontals executed: security (`@spaarke/auth`), test-quality (`/test-diet` + 138-failing reconciliation), CVE (no HIGH), observability (no PII in logs), doc-drift.
- [ ] Archived 12-item R3 draft fully reconciled — no item dropped silently.
- [ ] Portfolio: registered under Epic #427; `projects/INDEX.md` row added; NG1 filed as an Idea.
- [ ] Aggregate grade reaches **A+ (senior-panel standard)** with forcing-functions preventing re-drift.

---

*Program design: [design.md](design.md). AI spec: [spec.md](spec.md). Both preserved as project artifacts.*
