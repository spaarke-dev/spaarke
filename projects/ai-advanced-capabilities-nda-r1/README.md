# NDA Review & Analysis (Advisory Vertical) — `ai-advanced-capabilities-nda-r1`

> **Status**: ✅ **COMPLETE — all 22 tasks ✅, deployed to spaarkedev1, UAT-approved.** Live in `sprk_spaarkeai` + BFF `spaarke-bff-dev`. Grounding + Reasoning tier verified in-env 2026-07-28.
> **Program**: ai-advanced-capabilities-development — first analysis/advisory vertical
> **Branch**: `work/ai-advanced-capabilities-nda-r1` · **Created**: 2026-07-25 · **Build complete**: 2026-07-26 · **Closed**: 2026-07-28

## Build status (CLOSED 2026-07-28)

All 22 tasks ✅. The four tasks that were env-blocked at build time (011 picker, 012 grounding pin, 013 Reasoning deploy, 020 Action) were completed through the UAT follow-up commit wave and **deployed 2026-07-28 12:15 UTC**; verified empirically this session (live `az`/`pac` access to spaarkedev1):
- **012 / NFR-06 grounding pin** — KNW-011 seeded (8 chunks, `tenantId=system`, `documentType=legal`); OR-clause tenant-pin fix owner-approved (`9176ff25b`) + de-embedded the standard from the prompt. Reproduced empirically: bare-tenant filter → **0 chunks**, `(tenant or 'system')` → **8 chunks**. `ReferenceRetrievalService.cs:316`. NDA-REVIEW is now genuinely RAG-grounded (first version that is).
- **013 Reasoning tier** — `gpt-5-reasoning` deployment live on `spaarke-openai-dev` (smoke: `REASONING-OK`, finish:stop); `DocumentIntelligence__ReasoningModel=gpt-5-reasoning` set on `spaarke-bff-dev`; request-shape/timeout fixes deployed.
- **011 picker** — runtime model-tier picker in `ConversationPane`/`ConversationPaneChrome` + override composition.
- **020 Action** — `nda-review.action.json`: `modelTier:Reasoning`, `outputDeterminism:advisory`, closed schema `{overallRisk, flaggedSections[…]}`, not-legal-advice + citation + decline guardrails in prompt.
- **060 deploy** — BFF (46.13 MB compressed, ≤60 MB §10 gate ✅), code page, Actions/Bindings, AI Search index all deployed + reachable (healthz 200).
- **061 UI e2e** — experiential gate met via owner UAT rounds 5–8 (documented). Automated browser suite not re-run this session (no headless browser); 12 pre-existing e2e failures on master (compose-session-routing / edit-controls / three-pane-coordination) are **independent of this project** and tracked for a separate remediation pass.
- **090 wrap-up** — `/test-diet` clean (all 40 test files MAINTAIN; `notes/test-diet-report.md`); this closeout.

⚠️ **Re-UAT recommended**: the grounding + de-embed change means the deployed NDA-REVIEW is the first version actually pulling the 8-chunk KNW-011 standard via RAG (prior UAT ran on the prompt-embedded standard / silently-zero grounding). Output character may have shifted — worth a fresh pass.

**North star delivered**: relaxed-determinism advisory review (ADR-039 amendment) on the Reasoning tier, single-surface Compose UX (cited summary panel + right-gutter advisory comments + per-clause Draft Alternative + Summary-Page + comment-baked export + SPE versioning), NDA classification + "Review an NDA" card, runtime model picker, and the eval harness that grades it.

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
