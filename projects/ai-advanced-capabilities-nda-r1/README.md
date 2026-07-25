# NDA Review & Analysis (Advisory Vertical) — `ai-advanced-capabilities-nda-r1`

> **Status**: 🟡 Planning (artifacts generated; tasks pending `/task-create`)
> **Program**: ai-advanced-capabilities-development — first analysis/advisory vertical
> **Branch**: `work/ai-advanced-capabilities-nda-r1` · **Created**: 2026-07-25

## What this delivers

A non-lawyer uploads an NDA in the SpaarkeAi Assistant and receives a **Claude/ChatGPT-level advisory review** inside the **Compose editor (single surface)**: overall risk rating + concise cited flagged-section summary → right-gutter advisory Comments → user-driven per-section "Draft Alternative" rewrites using company standards → a generated **Summary Page** + comment-baked **Word export** → **SPE save with versioning**. Runs on the **Reasoning** model tier (with a runtime picker). Establishes the reusable advisory-vertical pattern for the program.

## Key decisions
- **ADR-039 amendment (task 001, merge gate)** — deterministic (fact, verbatim-cited) vs advisory (probabilistic, full reasoning, still prompt-controlled + cited + "not legal advice").
- **Model-tier selection** wired (`sprk_modeltier`) + runtime picker (`sprk_modeltieroverride`).
- **Compose is the single surface** — no separate Analysis widget.
- **Reuse-first** — SPE save, RAG (`spaarke-rag-references` + KNW-002), comment primitives, Draft Alternative, `ComposeShadowPatchEngine`. Net-new is small.
- **UC2 (fresh draft) deferred**; UC3 (standard summary) kept.

## Graduation criteria (from spec Success Criteria)
- [ ] End-to-end unaided non-lawyer flow: upload → card → Compose → Reasoning review → cited summary + gutter comments → Draft Alternative → Summary Page → SPE save + comment-baked export.
- [ ] NDA-REVIEW runs on the Reasoning deployment (verified).
- [ ] Advisory quality ≥ strong general LLM on the closed set (NFR-01 rubric).
- [ ] Every factual finding cites its NDA section; unverifiable claims declined.
- [ ] Grounding returns non-zero chunks under the pinned tenant (NFR-06).
- [ ] Negative/authorization cases pass (non-NDA declines, unreadable errors, unauthorized refused).
- [ ] ADR-039 amendment merged before advisory-tier code; golden-utterance eval green; BFF publish ≤60 MB.

## Artifacts
- [`spec.md`](spec.md) — AI implementation spec (43 FRs, 6 NFRs, §11 components, ADR Tensions)
- [`design.md`](design.md) — 6-lens use-case design
- [`PLAN.md`](PLAN.md) — WBS, discovered resources, dependency graph
- [`notes/spaarke-nda-standard-baseline.md`](notes/spaarke-nda-standard-baseline.md) — baseline NDA standard v0.1
- `tasks/` — POML task files (pending `/task-create`)

## Next step
`/task-create ai-advanced-capabilities-nda-r1` — decompose PLAN.md into POML tasks (task 001 = ADR-039 amendment gate).
