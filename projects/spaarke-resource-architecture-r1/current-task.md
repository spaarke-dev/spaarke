# Current Task — Spaarke Resource Architecture (R1)

> **Project status**: 🟡 **DEFERRED**
> **Last Updated**: 2026-05-23

---

## Current state

**No active task.** This project is in design-only state, awaiting the completion of [`sdap-bff-api-remediation-fix`](../sdap-bff-api-remediation-fix/) before execution begins.

## Why deferred

The BFF remediation project touches the same code surface this project would touch — specifically `Services/Ai/PublicContracts/` facade creation, AI job handler relocation, and BFF DI organization. Running two foundational refactors in parallel is the failure mode to avoid.

See [`README.md`](README.md) section "Why deferred" and [`design.md`](design.md) section 5 "Why this is deferred — the BFF remediation interaction" for the full reasoning.

## What to do when resuming

1. **Confirm sibling project completion**: Check that [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) has landed all five outcomes (A: size reduction, B: security hygiene, C: CI guardrails, D: codified prevention, E: internal AI hygiene).
2. **Re-read this project's design**: [`design.md`](design.md) and [`research/external-perspectives.md`](research/external-perspectives.md). The design was based on the pre-remediation BFF state; some specifics may need adjustment.
3. **Read post-remediation outputs**: New ADR(s) created by BFF remediation. The state of `Services/Ai/PublicContracts/`. Where the 18 Options classes ended up. Whether any of the 99 DI registrations got consolidated.
4. **Walk through the eight open questions** in [`design.md`](design.md) section 4 — several depend on the post-remediation BFF shape.
5. **Confirm scope and execution plan with owner**. Re-validate that Phase 1 (AI Search family) is still the right first slice. Some items may have been absorbed by remediation; some may need new approach.
6. **Begin Phase 0**: ADR draft, naming convention ratified, full resource-reference inventory across all 13 categories.

## What NOT to do

- Do not start writing code on this project until BFF remediation lands.
- Do not begin Phase 1 (AI Search family) before Phase 0 completes — the inventory + ADR + naming convention need to be ratified first.
- Do not skip the "re-validate design" step. Six weeks of BFF refactoring will change things.
- Do not let the standalone YAML manifest idea sneak back in. It was deliberately rejected after external review. The `.bicepparam` + typed catalogs + generated solution constants ARE the manifest.

## History

- **2026-05-23** — Project scaffolded. AI Search regression investigation surfaced 184 references for one resource identifier across 67 files. External review by DeepSeek, ChatGPT, Gemini, Claude validated the broad shape but recalibrated the approach: dropped the standalone YAML manifest (Claude's reframing), added Azure Deployment Stacks as prevention layer (Claude's addition), kept the typed per-family catalog approach (all four agreed). Deferred execution pending BFF remediation completion. No code written.

## Project resumption checklist

When this section gets updated with an active task, the following must be true:

- [ ] `sdap-bff-api-remediation-fix` marked complete in its own `current-task.md`
- [ ] This project's design re-validated against post-remediation BFF state
- [ ] All 8 open questions in `design.md` §4 resolved
- [ ] Owner sign-off on scope and Phase 1 execution plan
- [ ] Phase 0 work scoped (ADR draft, naming convention, inventory)
