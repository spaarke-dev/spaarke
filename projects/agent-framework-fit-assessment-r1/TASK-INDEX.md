# Task Index — Agent Framework Fit Assessment R1

> **Legend**: 🔲 pending · ▶️ in progress · ✅ complete · ⚠️ blocked/gap

Canonical plan: [`SPEC.md`](./SPEC.md) · Project conventions: [`CLAUDE.md`](./CLAUDE.md)

---

## Phase progress

| # | Phase | Status | Notes |
|---|---|---|---|
| 0 | Primary-source baseline (task 000) | ✅ | Done 2026-06-03. SHA afa7834e (1.9 release). 34 primary sources, 100% within recency floor. Critical findings: AF 1.0 GA April 2026; #6268 streaming bug affects S1; massive sample catalog expansion. |
| 1 | Inventory current state (tasks 001-002) | ✅ | Done 2026-06-03 (parallel-group A, both commits landed). 001: S1 only Extensions.AI user; S2/S3/S4 use IOpenAiClient or raw OpenAI SDK; Microsoft.Agents.AI package referenced but ZERO source usage; 2 S8 surfaces discovered (SessionSummarizationService, CapabilityRouter) recommended for S1 perimeter. 002: S5 is BIMODAL — shipped in-BFF Foundry wrapper (Services/Ai/Foundry/, ADR-018 default-OFF) + planned canonical durable surface (curated only); S6 uses M365 Agents SDK not Agent Framework; S7 MCP deferred to Phase 2. |
| 2 | Agent Framework feature mapping (task 003) | ✅ | Done 2026-06-03 (commit 68d35c73). 12 features (F1-F12) × 4 subsections; 19 distinct primary-source citations (12 Learn + 1 Devblog + 4 sample-tree + 2 GitHub Issues); 94.7% recency. Top findings: S1 lift gated on #6268; S2 lift to Workflows is binary; Tool Approval (F11) unifies framework + workflow HITL; S5 Foundry adds hosting not HITL; S6/S7 are forward-compat. |
| 3 | Per-surface decision analysis (task 004) | ✅ | Done 2026-06-03 (commit d473c023). 10 surfaces evaluated (S5 bifurcated into A/B + 2 S8 discoveries from task 001); distribution: 1 ADOPT (S5B canonical durable legal HITL) / 5 PARTIAL / 4 DON'T ADOPT. Anti-bias guard rail passes decisively. Key verdicts: S1 PARTIAL gated on #6268; S2 JPS DON'T ADOPT (Workflows non-incremental migration); S5B is sole ADOPT (workflow HITL is framework-internal, narrowing Foundry-vs-framework to deployment-only); S6 DON'T ADOPT (uses M365 Agents SDK, distinct from Agent Framework). |
| 4 | Deployment + migration (task 005) | ✅ | Done 2026-06-03 (commit 45b93668). Deployment models per ADOPT/PARTIAL surface: S1/S3/S5A/S8a/S8b in-process BFF (ADR-013 criteria fail); S5B mixed (3 candidates — prototyping required, LOW confidence due to F12 gap); S7 deferred to D-A20 contract. Publish-size: BASELINE preserved (45.65 MB → 47-54 MB worst-case), `Microsoft.Agents.AI` already referenced means S1/S3/S5A/S8a/S8b lifts have net-zero size impact. 10 risks (3 HIGH: #6268, F12 evidence gap, S5B mis-scoping). Cost: 8-17 person-weeks Phase 1-3 (excluding S5B greenfield + S7 deferred), confidence LOW-MED. |
| 5 | Synthesis (task 006) | ✅ | Done 2026-06-03 (commit cdaab907). 893-line canonical assessment at docs/assessments/agent-framework-fit-assessment-2026-06-03.md. All 10 sections, 18 live primary URL citations + 36-row §10 Sources appendix, 100% recency floor compliance, 6 open questions, declarative tone. Quality gates: adr-check ✅ no violations (2 forward-looking warnings on implied ADR-013 amendment + S5B prototyping correctly deferred); code-review ✅ accepted with 2 warnings (W1 S8a verdict change vs notes/04, W2 undisclosed judgment calls — both routed to task 007 adversarial review). |
| 6 | Review + sign-off (tasks 007-008) | 🔲 | Adversarial review + source recency re-check + sign-off + unblock note for agent-framework-knowledge-r1 |

## Tasks

| ID | Title | Phase | Rigor | Parallel group | Status | Owner |
|---|---|---|---|---|---|---|
| [000](tasks/000-refresh-primary-sources.poml) | Refresh primary sources baseline (recent-content-only) | 0 | STANDARD | — | ✅ | Claude (2026-06-03) |
| [001](tasks/001-inventory-spaarke-ai-surfaces.poml) | Inventory Spaarke AI code surfaces (S1-S4 + S8 catch) | 1 | STANDARD | A | ✅ | Claude (2026-06-03, sub-agent commit cb883dd9) |
| [002](tasks/002-inventory-non-bff-ai-touchpoints.poml) | Inventory non-BFF AI touchpoints (S5-S7) | 1 | STANDARD | A | ✅ | Claude (2026-06-03, sub-agent commit dae72474) |
| [003](tasks/003-map-agent-framework-features.poml) | Map Agent Framework feature surface vs. Extensions.AI baseline | 2 | STANDARD | B | ✅ | Claude (2026-06-03, sub-agent commit 68d35c73) |
| [004](tasks/004-per-surface-decision-analysis.poml) | Apply decision criteria to each surface; produce per-surface matrix | 3 | STANDARD | — | ✅ | Claude (2026-06-03, sub-agent commit d473c023) |
| [005](tasks/005-deployment-and-migration-analysis.poml) | Deployment model recommendations + aggregated migration cost analysis | 4 | STANDARD | — | ✅ | Claude (2026-06-03, sub-agent commit 45b93668) |
| [006](tasks/006-write-assessment-document.poml) | Synthesize findings into docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md | 5 | FULL | — | ✅ | Claude (2026-06-03, sub-agent draft commit cdaab907 + main-session Step 9.5 quality gates) |
| [007](tasks/007-adversarial-review.poml) | Adversarial review + source recency re-check; revise as needed | 6 | FULL | — | 🔲 | — |
| [008](tasks/008-project-wrap-up.poml) | Sign-off + unblock note for agent-framework-knowledge-r1 | 6 | MINIMAL | — | 🔲 | — |

## Parallel execution groups

- **Task 000** runs FIRST sequentially — all downstream tasks depend on the primary-source baseline it produces.
- **Group A** (tasks 001, 002): Both inventory tasks, independent sources (BFF code vs. non-BFF projects). Both depend on 000. Safe to fan out in parallel after 000 lands via two `task-execute` Skill calls in one message.
- **Group B** (task 003): Independent of A but depends on 000. Safe to start in parallel with A after 000 lands.
- **Tasks 004, 005, 006, 007, 008** sequential — each depends on the previous.

## Gaps / blocks log

- **2026-06-03 (task 002)**: SPEC §3 S5 row was factually wrong — claimed "no Spaarke production code yet" but task 002 found a shipped in-BFF Foundry wrapper at `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/` (5 .cs files, default-OFF kill switch per ADR-018, consumed by `AgentServiceRoutingMiddleware` + JPS `AgentServiceNodeExecutor`). **Action**: SPEC.md S5 row + CLAUDE.md S5 row updated 2026-06-03 to describe S5 as bimodal (shipped wrapper + planned canonical surface). Task 004 decision matrix must address BOTH facets.
- **2026-06-03 (task 001)**: Two S8 surfaces discovered via Grep — `Services/Ai/Sessions/SessionSummarizationService.cs` and `Services/Ai/Capabilities/CapabilityRouter.cs` both use `IChatClient` outside the inventoried S1/S2/S3/S4 perimeters. Task 001 inventory recommends folding into S1 perimeter; task 004 must apply the decision matrix to them.
- **2026-06-03 (task 001 + #6268)**: GitHub Issue #6268 (.NET `ChatClientAgent.RunStreamingAsync` ends with no assistant text on multi-tool turns) — RED FLAG carried into S1 inventory. Task 004's S1 ADOPT recommendation must condition on resolution/workaround; task 007 must re-fetch the issue at adversarial-review time and re-evaluate.
- **2026-06-03 (task 003 evidence-thin)**: Two feature-map sections flagged as evidence-thin for task 006 re-check: (a) F3 Context providers — no standalone Learn `/agents/context-providers/` page in fetched sources; relied on overview + sample tree; (b) F12 Durable hosting — no dedicated `/hosting/` Learn page in notes/00; relied on `04-hosting/` sample tree + Devblog D6 + open GitHub Issue #6308. Task 005 (deployment + migration) must address the durable-hosting gap explicitly.
- **2026-06-03 (task 006 quality-gate warnings, route to task 007)**: (W1) Synthesis doc §5.9/§5.11 changed S8a verdict from notes/04's PARTIAL to DON'T ADOPT based on deeper analysis ("textbook anti-fit; single-call summarizer, F5 has qualitative regression risk"). Divergence is defensible but not disclosed inline — task 007 adversarial review must either accept the re-evaluation explicitly or restore PARTIAL. (W2) Synthesis includes two judgment-call extensions beyond notes/05: (a) framing middleware lift as "implied ADR-013 amendment" in §7.1+§9.2; (b) bundling Q5+Q6 into §8 (POML required ≥3 — synthesis included 6). Both defensible; task 007 may decide whether inline disclosure footnotes are warranted.

## Reference

- Canonical plan: [`SPEC.md`](./SPEC.md)
- Project conventions: [`CLAUDE.md`](./CLAUDE.md)
- Status overview: [`README.md`](./README.md)
- Current task state: [`current-task.md`](./current-task.md)
- Parked downstream project: [`../agent-framework-knowledge-r1/`](../agent-framework-knowledge-r1/)
