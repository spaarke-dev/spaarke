# Spaarke AI Platform Unification R5

> **Status**: Pre-planning — user testing + FR feedback work scoped at session kickoff
> **Created**: 2026-06-03
> **Predecessor**: [spaarke-ai-platform-unification-r4](../spaarke-ai-platform-unification-r4/) — shipped to master at `18b9323f` on 2026-06-03
> **Repo state at R5 kickoff**: master after PR #331 (R4) + PR #332 (Trivy cleanup) + PR #333 (skill update)

---

## Purpose

R5 begins as a **user testing + feedback capture** round for SpaarkeAi functional requirements. Scope, deliverables, and final task list are determined at the kickoff session (intentionally not pre-decided — the testing approach itself depends on operator priorities).

## Status

Empty by design. Pre-planning. The kickoff session will produce:
- A scoping decision (which surfaces, audience, cadence, deliverable shape)
- Either a `design.md` (for the full pipeline) OR a working notes pack (for a one-off study), based on scope
- A user testing instrument (script, observation checklist, capture template) if scope warrants

## Quick Links — Read These at Kickoff

The kickoff session should load these as primary context:

### R5-specific (this folder)
- [`notes/ground-truth-spaarkeai-state.md`](notes/ground-truth-spaarkeai-state.md) — **READ FIRST**: actual shipped behavior of `sprk_spaarkeai` as of R4 merge. Code-grounded survey (not architecture aspirations). Entry points, widget catalog, mount sources, FR-to-surface map, suggested initial test focus.
- [`notes/user-testing-kickoff.md`](notes/user-testing-kickoff.md) — scoping questions + recommended kickoff prompt for the new session

### Canonical architecture (master)
- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — two-wrapper architecture (Dashboard wrapper vs Direct widget wrapper); four mount sources; required reading
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — end-to-end pipeline (cold-load → render → persistence)
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — `@spaarke/*` library inventory + PaneEventBus contract
- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — 5 archetypes with decision tree (R4 rewrite)

### Requirements baseline
- [`projects/spaarke-ai-platform-unification-r4/spec.md`](../spaarke-ai-platform-unification-r4/spec.md) — R4 FRs/NFRs/DRs/PRs (14/9/7/2)
- [`projects/spaarke-ai-platform-unification-r4/notes/lessons-learned.md`](../spaarke-ai-platform-unification-r4/notes/lessons-learned.md) — what shipped, what surprised, what was deferred (R5 backlog items live here)
- [`projects/spaarke-ai-platform-unification-r3/spec.md`](../spaarke-ai-platform-unification-r3/spec.md) — predecessor FRs still in force
- [`projects/spaarke-ai-platform-unification-r3/notes/lessons-learned.md`](../spaarke-ai-platform-unification-r3/notes/lessons-learned.md) — R3 lessons recommended

## Sibling Projects (cross-references)

| Project | Status | Relationship to R5 |
|---|---|---|
| [`projects/spaarke-iframe-wizard-pattern-enhancement/`](../spaarke-iframe-wizard-pattern-enhancement/) | Design (pre-planning) | Captures cross-surface "mount workspace tab" pattern; W-5 implementation gap surfaced in R5 testing may feed this project's prioritization |
| `projects/spaarke-graph-sdk-kiota-upgrade-r1/` (placeholder) | Not started | Deferred Kiota CVE-2026-44503 lives here; no R5 dependency unless backend FRs test against Graph SDK behavior |
| [`projects/trivy-cve-backlog-cleanup-r1/`](../trivy-cve-backlog-cleanup-r1/) | ✅ Shipped 2026-06-03 | Trivy backlog cleared during R4 close-out; security gate is back ON; no R5 dependency |

## Graduation Criteria (placeholder — finalize at kickoff)

To be defined during the kickoff session. Candidate criteria:

- [ ] Scope agreed (surfaces, audience, cadence, deliverable)
- [ ] Test instrument produced (script + capture template + observation checklist if applicable)
- [ ] Testing sessions executed (count depends on scope)
- [ ] Feedback synthesized → R5 backlog of FR/UX issues with severity + recommended owner
- [ ] R6 (or follow-on) project scoped from findings (or N/A if findings are absorbed into existing backlogs)

## Changelog

| Date | Entry |
|---|---|
| 2026-06-03 | Project folder created (operator). README + ground-truth survey + kickoff prompt drafted post-R4 close. Kickoff to happen in a new session with the documents in `notes/` loaded as primary context. |
