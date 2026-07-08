# Spaarke AI Architecture Redesign R2 (Core)

> **Status**: Planning → Ready for Tasks
> **Last Updated**: 2026-07-08
> **Parent epic**: #421 SPAARKE AI
> **Type**: BFF / AI-platform core (judgment + memory)
> **Complexity**: High (51 FRs; sole owner of `Services/Ai/` internals)

---

## What this project delivers

The **platform core** of the R2 AI redesign — refining R1's coarse-grained platform into a refined experience along two owner-prioritized axes, built strictly ON ADR-039 + ADR-040:

1. **Judgment + friction** (G-R2-A) — a Resourcefulness Doctrine (verify → act → degrade gracefully → refuse-with-affordance), deterministic Confirmation Policy v2, first-class Completion UX (OutcomeCard, job-aware), UI-action truthfulness, decision traceability + live plan narration, progressive render.
2. **Memory** (G-R2-B) — a five-scope Memory Service (extending the existing `MatterMemoryService`), a Context Binder assembling one governed `ContextEnvelope` per turn, memory-as-governed-side-effect with a poisoning threat model.
3. **Hardening** (G-R2-D) — publish-size, eval-gate, cross-satellite seam-fork verification, and the inherited backlog.

Plus **seven contract-first seams** (Phase A0) the parallel **Compose r2** satellite consumes, and **ADR-041 + ADR-042** authored + promotion-gated.

## What's OUT of scope

- Compose editor + document lifecycle → **Compose r2** satellite (core ships seams + enforces the ingestion-parity invariant).
- Daily Briefing remediation → **separate project**.
- Insights Engine Widget refurbish → separate satellite after core Phase A.
- Work IQ / Foundry IQ runtime → interface only; spike deferred.
- Multi-agent orchestration, Fabric, goal-tracking subsystem, new manifest Dataverse tables, re-opening R1's architecture.

## Graduation criteria (browser-UAT-gated on spaarkedev1)

- [ ] **G-R2-A** — explicit+complete write executes with no dialog + ✅ + record chip + next-step chips; ambiguous/inferred confirms exactly once; blocked requests verify-then-partial-value + working deep link; claimed UI actions backed by real client events; traceability view + live narration; progressive render.
- [ ] **G-R2-B** — assistant knows user/record/conversation/workspace/preferences without re-prompting; preferences persist across sessions; user can see + delete memory; a hostile document cannot write memory.
- [ ] **G-R2-D** — reliable, telemetered, eval-gated, publish-size verified (≤60 MB), codebase not larger than r1 left it; no satellite forked an AI-internal seam.
- [ ] **ADR-041 + ADR-042** Accepted at their gates.
- [ ] **Contract-first** — all 7 A0 contracts have a contract test.

## Key artifacts

- [design.md](./design.md) (v0.4) — charter
- [spec.md](./spec.md) — 51 FRs, browser-UAT-gated
- [plan.md](./plan.md) — WBS + component justification + discovered `file:line` anchors
- [notes/d-f0-eval-family-spec.md](./notes/d-f0-eval-family-spec.md) · [notes/policy-v2-origin-classification-decision-tree.md](./notes/policy-v2-origin-classification-decision-tree.md) — pre-spec enforcement inputs
- [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) — task tracker (created by task-create)

## Coordination

Parallel peer **`spaarkeai-compose-r2`** (BFF+SpaarkeAi) consumes this core's seams. The core-owns-AI-internals rule + `/conflict-check` + `projects/INDEX.md` keep them non-colliding. Compose r2 has already re-based onto the core seams (its 2026-07-08 revision).
