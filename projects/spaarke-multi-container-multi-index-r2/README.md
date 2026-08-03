# Spaarke Multi-Container Multi-Index Routing — R2

> **Status**: Scope review (pre-spec)
> **Last Updated**: 2026-06-27

---

## Purpose

R2 is the follow-on to [`spaarke-multi-container-multi-index-r1`](../spaarke-multi-container-multi-index-r1/). R1 shipped functional per-record container + index routing across the wizard / BFF / PCF / code-page stack, but left scope extensions partially shipped, an architectural seam half-wired, and several known gaps and lessons that warrant a deliberate R2 scope decision.

## Current state

| Field | Value |
|---|---|
| **Phase** | Pre-spec — R1 synopsis review |
| **Branch** | not yet created |
| **Spec** | not yet drafted |
| **Plan** | not yet drafted |
| **Tasks** | not yet decomposed |

## Inputs for R2 scope decision

| Document | Purpose |
|---|---|
| [`notes/r1-synopsis.md`](./notes/r1-synopsis.md) | What R1 shipped, scope extensions, deferred items, open issues, lessons — the working brief for the R2 scope discussion |
| [`../spaarke-multi-container-multi-index-r1/`](../spaarke-multi-container-multi-index-r1/) | R1 project folder — spec.md, design.md, plan.md, notes/lessons-learned.md, all handoffs |

## Next steps

1. Review [`notes/r1-synopsis.md`](./notes/r1-synopsis.md)
2. Decide R2 scope (what to finish vs defer to R3 vs deprecate)
3. Draft `spec.md` once scope is agreed
4. Run `/project-pipeline` from the spec to generate `plan.md`, `CLAUDE.md`, `current-task.md`, tasks/

---

*Created 2026-06-27 as a scope-review shell. No implementation work begins until spec is signed off.*
