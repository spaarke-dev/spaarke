---
name: use-case-to-design
description: When starting a new AI-capability project driven by a concrete legal/business use case (e.g., "NDA review", "lease analysis", "case-law research"): runs a structured 6-lens method — use case → surface/UX → required AI capabilities → have-vs-gap → configuration → acceptance — and emits a complete design.md ready for design-to-spec. Use before design-to-spec, not instead of it.
tags: [project-init, design, use-case, ai-capabilities, planning, spaarke-ai]
techStack: [dotnet, react, dataverse, spaarke-ai]
appliesTo: ["projects/*/design.md", "use case to design", "define a new AI use case", "ai-advanced-capabilities-*"]
alwaysApply: false
exemplar: .claude/skills/use-case-to-design/references/design-template.md
last-reviewed: 2026-07-21
---

# Use-Case → Design

> **Category**: Project-orchestration
> **Last Reviewed**: 2026-07-21

## When to Use

When the user wants to enable a **specific AI use case** on the Spaarke platform — a named legal/business capability like "NDA review/analysis/drafting", "commercial-lease analysis", "case-law research", "invoice intake" — and needs a rigorous, repeatable way to flesh out *everything the design must contain* before spec/plan/task generation.

Auto-detect / invoke when:
- The user names a use case as the driver of a new project (e.g., "our first is `ai-advanced-capabilities-nda-analysis-r1`").
- A project folder exists (or is about to) under the `ai-advanced-capabilities-*` program, or any project whose value is defined by a use case rather than a horizontal capability.
- The user asks to "define the use case", "write the design for X", or "figure out what we need to build for Y".

This skill produces `design.md`. It is the **upstream feeder to `design-to-spec`** — it does NOT write spec.md, plan.md, or tasks. Handoff chain: **use-case-to-design → design-to-spec → project-pipeline**.

## What to Achieve

Produce a complete, decision-forcing `projects/{name}/design.md` by working the **6 lenses** in order. Each lens is a required section; skipping one leaves a gap that surfaces as flailing tasks later.

1. **Use Case** — the jobs to be done (review / analysis / drafting / research / triage), personas, triggers, inputs→outputs, concrete sub-tasks, scope boundaries + explicit non-goals, done-criteria, business value.
2. **Surface / UX** — target surfaces (SpaarkeAi workspace · Compose/Tiptap editor · Context pane · SprkChat · DataGrid), a concrete end-to-end interaction walk-through, reused-vs-new UI, and the required states (loading / empty / error / **uncertainty**).
3. **AI Capabilities Required** — decompose the use case into the capability primitives it needs, named against the **real** model: Actions (prompted/coded) · Skills (prompt fragments) · Tools (`sprk_analysistool`) · Knowledge/RAG sources · Bindings + dispositions · Memory scopes · citation/grounding · redline · retrieval · gates. Frame as "for this to work, the AI service MUST be able to…".
4. **Have vs. Gap** — score EACH required capability against current code state as **REUSE** (done) · **ACTIVATE** (built-but-dark) · **COMPLETE** (partial) · **BUILD** (absent), with file evidence. Use `references/capability-lenses.md` for the checklist + authoritative source pointers; run a live capability audit (Explore agents) when uncertain.
5. **Configuration** — the concrete seed set that turns existing components into this use case: which Action rows, Binding rows, Knowledge/reference docs, grid configs, capability grants, model tiers, and ported prompt content (with license attribution if adapted, e.g. Mike OSS MIT).
6. **Acceptance & Evaluation** — a **closed** test set (input docs + expected outputs, incl. negative/authorization cases) and the eval-harness cases (`legal-eval-config.yaml`, `metrics/citation_accuracy.py`). Non-optional — Spaarke's Actions are otherwise untested.

Plus a **Governance Seeds** block so `design-to-spec` has clean inputs: `<hot-path-declaration>`, §11 new-component three-question table, candidate ADR tensions, and the **platform-enabler flag** (does this use case *demand-pull* a shared capability?).

## Method (how to run it)

1. Confirm/parse the use case and target project folder. If no folder, note that `devops-project-start` or a folder scaffold is a prerequisite.
2. Load `references/design-template.md` (the fill-in structure) and `references/capability-lenses.md` (the have-vs-gap checklist).
3. Work lenses 1→3 with the user, asking **targeted** questions (like design-to-spec Step 2.5) only where an answer changes the design.
4. For lens 4, verify have-vs-gap against the live codebase — check `projects/ai-advanced-capabilities-development/PROGRAM-ROADMAP.md` §1 (current-state inventory) first; spawn parallel Explore agents for anything not covered there. Never assert DONE/ABSENT from memory.
5. Fill lenses 5–6 and the Governance Seeds block.
6. Write `design.md`; present the have-vs-gap summary + any BUILD items (§11-justified) + demand-pulled platform enablers for review.
7. Hand off: "design.md ready — run `/design-to-spec {project}` when approved."

## Constraints (MUST / MUST NOT)

- **MUST** default to the capability precedence **REUSE > ACTIVATE > COMPLETE > BUILD**. Every **BUILD** item requires a §11 three-question justification (existing overlap / can-extend / concrete cost-of-doing-nothing) in the Governance Seeds block.
- **MUST** name capabilities against the real, post-redesign model (Actions + Bindings + Tools over the ADR-039 closed catalog / ADR-040 ledger dispatch spine) — **NOT** the retired node-graph playbook engine. Re-express any "new playbook node" as an Action/Tool/Binding/ledger-reader.
- **MUST** verify every have-vs-gap verdict against code (roadmap §1 or an Explore audit). No memory-based DONE/ABSENT claims. Evidence: the `MemoryCompositionService` dark-capability finding — capability built without a use-case consumer stays unwired and unvalidated (`PROGRAM-ROADMAP.md` §1.2).
- **MUST** keep horizontal capability **demand-pulled**: a shared platform enabler (scheduler, `IGateResolver`, model-tier, EvaluatorGate, `sprk_analysis` results table) is only pulled through by the *first* use case that needs it, at minimal increment; do not pre-build. Promote to a standalone enabler project only when a *second* use case needs it identically.
- **MUST** capture license attribution in the Configuration lens when adapting external content (Mike OSS workflows = MIT; Lavern = Apache-2.0 patterns only; CUAD/MAUD = CC BY; UNFAIR-ToS/LEDGAR = CC BY-SA → legal review).
- **MUST NOT** write spec.md / plan.md / tasks — that is `design-to-spec` and `project-pipeline`. This skill stops at `design.md`.
- **MUST NOT** leave any of the 6 lenses empty; an unknowable lens is recorded as an explicit Unresolved Question, not omitted.

## Acceptance Criteria

- [ ] `design.md` exists with all 6 lenses + Governance Seeds populated.
- [ ] Every required capability in lens 3 has a lens-4 verdict (REUSE/ACTIVATE/COMPLETE/BUILD) with file evidence.
- [ ] Every BUILD item has a §11 three-question justification.
- [ ] Configuration lists concrete seed rows/docs/grants (not "configure the Actions").
- [ ] Acceptance lens has a closed test set incl. ≥1 negative/authorization case.
- [ ] `<hot-path-declaration>` present if BFF/SpaarkeAi touched; demand-pulled platform enablers flagged.
- [ ] Handoff to `design-to-spec` stated; no spec/plan/tasks written.

## Reference Exemplar

- See `references/design-template.md` — the fill-in design.md structure this skill produces. The first real worked example will be `projects/ai-advanced-capabilities-nda-analysis-r1/design.md` (add as `examples/nda-analysis.md` once complete).

## Gotchas

- **The dark-capability trap**: Spaarke has significant AI capability that is built + DI-registered but has **zero runtime callers** (e.g., `MemoryCompositionService.ComposeAsync` → pinned memory never reaches the model). If lens 4 marks something ABSENT that is actually ACTIVATE, you will propose a wasteful rebuild. Always check `PROGRAM-ROADMAP.md` §1 + code first. Evidence: `PROGRAM-ROADMAP.md` §0/§1.2.
- **Vocabulary drift**: older design docs (e.g., `LAVERN-ANALYSIS-AND-PLAN.md`, 2026-05-20) predate the AI redesign and describe a retired playbook engine. Re-base capability names onto Actions/Bindings/Tools; do not copy the old vocabulary into design.md. Evidence: `PROGRAM-ROADMAP.md` §0.
- **Horizontal-first regression**: it is tempting to scope "wire the memory system" or "build the gate framework" as its own project. That reproduces the dark-capability trap. Keep the use case the driver; let it pull the minimal capability slice. Evidence: this skill's `MUST` on demand-pull.
- **Untested prompts assumed good**: every existing Action/JPS prompt was LLM-authored and never eval'd. Do not treat "the Action exists" as "the Action works" — lens 6 must eval it. Evidence: `reference_mike-oss-legal-ai-assessment` (memory).

## References

- **Template**: `references/design-template.md` — the design.md structure to fill.
- **Have-vs-gap checklist**: `references/capability-lenses.md` — capability primitives + where to verify each in code.
- **Current-state inventory**: [`projects/ai-advanced-capabilities-development/PROGRAM-ROADMAP.md`](../../../projects/ai-advanced-capabilities-development/PROGRAM-ROADMAP.md) §1 — REUSE/ACTIVATE/COMPLETE/BUILD source of truth.
- **Downstream skill**: [`design-to-spec`](../design-to-spec/SKILL.md) — consumes the design.md this skill writes.
- **Governance**: root [`CLAUDE.md`](../../../CLAUDE.md) §10 (BFF hygiene), §11 (component justification), §6.5 (ADR tensions).
- **AI model**: [`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`](../../adr/ADR-039-grounded-execution-closed-catalogs.md), ADR-040, ADR-043; `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`.

---

*use-case-to-design is the use-case-vertical front door to the project pipeline: it forces a complete design.md by working 6 lenses, defaults to reuse/activate over build, and hands off to design-to-spec. It writes design.md and nothing downstream.*
