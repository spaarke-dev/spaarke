# Current Task — Agent Framework Fit Assessment R1

> Tracks ACTIVE task only. History lives in `TASK-INDEX.md` and per-task `.poml` files.

---

**Active task**: none — Phase 3 complete (tasks 000, 001, 002, 003, 004 all ✅). Ready for Phase 4.
**Next task**: task 005 (sequential — depends on 004) — deployment model + aggregated migration cost / risks.

**How to start**: from a fresh session, type `work on task 005` and the harness will invoke `task-execute` with the POML.

---

## Last completed task

### Task 004 — Per-surface decision matrix
- **Output**: `projects/agent-framework-fit-assessment-r1/notes/04-per-surface-decision-matrix.md` (708 lines)
- **Commit**: `d473c023`
- **Surfaces evaluated**: 10 (S1, S2, S3, S4, **S5A shipped wrapper**, **S5B canonical durable**, S6, S7, S8a, S8b)
- **Distribution**: 1 ADOPT / 5 PARTIAL / 4 DON'T ADOPT (anti-bias guard rail passes decisively)

---

## Per-surface verdicts (carry forward to tasks 005/006)

| Surface | Verdict | One-line reason |
|---|---|---|
| **S1** SprkChat | **PARTIAL** | Highest structural fit but gated on Issue #6268 (multi-tool streaming bug affects canonical workload) |
| **S2** AnalysisOrchestration + JPS | **DON'T ADOPT** | Workflows is conceptually competitive but non-incremental; rewrite touches 30+ files + Dataverse schema |
| **S3** Builder | **PARTIAL** | Manual agentic loop benefits from AIAgent base + Tool Approval, but uses OpenAI.Chat SDK directly today |
| **S4** Background AI jobs | **DON'T ADOPT** | Pure server-side LLM via Service Bus; AIAgent base adds little over direct LLM calls |
| **S5A** Foundry wrapper (shipped) | **PARTIAL** | Already in production, default-OFF per ADR-018; lift to `Microsoft.Agents.AI` only if S5B path is taken |
| **S5B** Foundry canonical durable HITL (planned) | **ADOPT** ⭐ | The only ADOPT. F7 Workflows + F11 RequestPort + F12 Foundry hosting purpose-built for multi-day legal HITL |
| **S6** M365 Copilot | **DON'T ADOPT** | Uses M365 Agents SDK (`Microsoft.Agents.Builder`), distinct from Agent Framework — don't conflate |
| **S7** Insights Engine MCP | **PARTIAL** | When Phase 2 implementation lands, may host Agent Framework agents — contract D-A20 decides |
| **S8a** SessionSummarizationService | **PARTIAL** | Already on `IChatClient` — folds into S1 perimeter once #6268 unblocks |
| **S8b** CapabilityRouter | **PARTIAL** | Same as S8a; raw IChatClient classifier — fold into S1 |

---

## Top 3 conclusions feeding task 006 synthesis (§executive summary)

1. **S2 JPS is DON'T ADOPT** — Workflows is conceptually competitive but no incremental migration path exists; wholesale rewrite touches 30+ files + Dataverse schema + plugin pipeline. Cost grossly disproportionate to benefit on a working production system.
2. **S5B (canonical durable legal HITL) is the only ADOPT** — F7 Workflows + F11 `RequestPort` + F12 Foundry hosting purpose-built for multi-day legal workflows. **Important**: Workflow HITL is in the framework itself (not Foundry-exclusive), narrowing the Foundry-vs-framework choice to deployment-only (where Foundry adds VM isolation, per-agent Entra identity, A2A endpoint).
3. **S1 PARTIAL gated on Issue #6268** — Structural fit is the highest in the assessment but the bug affects SprkChat's canonical multi-tool streaming workload; adopting before #6268 lands in a shipped 1.x release would ship a regression.

---

## Top open questions for synthesis §8 (human-decision)

1. **S5B VM-isolation requirement**: Do Spaarke legal workflows actually require per-session VM isolation + per-agent Entra identity + A2A exposure? Determines Foundry-hosted vs Workflows-in-BFF/Function deployment for the sole ADOPT verdict.
2. **S1 wait-or-pilot timing**: Wait for Issue #6268 to land in a shipped 1.x release, or pilot with a feature flag + fallback now? Depends on % of SprkChat traffic that is multi-tool.
3. **S7 D-A20 contract authoring**: When the deferred MCP server contract is written, MUST address (a) host library, (b) deployment model, (c) BFF seam — three concrete UNKNOWNs that cannot be pre-committed.

---

## Inputs for task 005 (deployment + migration analysis)

Task 005 consumes the matrix and produces:
- **Deployment model recommendation per ADOPT/PARTIAL surface**: in-process BFF (default per ADR-013) / MCP server / Azure Function / Hosted Foundry. The S5B Foundry-hosted vs Workflows-in-BFF question is the central deployment-model decision.
- **Aggregated migration cost** across S1+S3+S5A+S7+S8a+S8b (PARTIAL surfaces): publish-size impact (per `.claude/constraints/bff-extensions.md`), CVE risk, test impact, OTel/AppInsights preservation.
- **Phasing recommendation**: which surface adopts first? S5B is greenfield (no migration cost); S1 is gated on #6268; S8a/S8b are folded into S1's wave.
- **Address the evidence-thin gaps from task 003**: F3 Context providers + F12 Durable hosting (especially F12 because S5B depends on durable hosting decisions).

---

## Citation discipline (still binding)

- Every claim cites notes/01, notes/02, notes/03, or notes/04 with section reference
- ≥80% of primary-source citations dated 2026-04-01 onwards
- §10 Sources appendix in the final assessment document is mandatory

---

## Phase status

- Phase 0 ✅ (task 000 — primary-source baseline)
- Phase 1 ✅ (tasks 001, 002 — inventory)
- Phase 2 ✅ (task 003 — feature map)
- Phase 3 ✅ (task 004 — decision matrix)
- Phase 4 🔲 (task 005 — deployment + migration)
- Phase 5 🔲 (task 006 — synthesis = the canonical assessment document)
- Phase 6 🔲 (tasks 007, 008 — adversarial review + sign-off)
