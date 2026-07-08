# Current Task State — spaarke-ai-platform-unification-r7

> **Last Updated**: 2026-07-05 (Fable 5 session — audit Step 1 COMPLETE)
> **Recovery**: read Quick Recovery, then `projects/spaarke-ai-code-audit-r1/SPAARKE-AI-CODE-INVENTORY.md`

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Session** | Fable 5 session executed **Steps 1 AND 2 of the 3-step code audit** (operator-directed 2026-07-05). Step 1: `projects/spaarke-ai-code-audit-r1/` + **`SPAARKE-AI-CODE-INVENTORY.md` v1.0** (7 parallel Explore agents). Step 2: **canonical doc §4-7 drafted → v0.3**, designed against the inventory. |
| **Branch** | `work/spaarke-ai-platform-unification-r7` |
| **Canonical design doc** | `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` **v0.3** — §4 (five layers, 3 execution shapes), §5 (21-component map K/E/N/R + O-1..O-24 resolution), §6 (extend sprk_playbookconsumer + sprk_analysistool, no new tables, single-routing-surface rule), §7 (L0-L4 protocol, ten-mechanism disposition table, P1-P10 replay). §8 roadmap deferred to Step 3. |
| **Next Action** | **All 3 audit steps + convergence COMPLETE.** Canonical doc = **v0.4 CONVERGED TARGET** (all OQs resolved, D1-D12 ratified as amended, E-1..E-5 ruled). Next: **spin up the implementation project** — `/design-to-spec` over canonical v0.4 + `SPAARKE-AI-MIGRATION-MAP.md` (P0-P4 phases) → `/project-pipeline` (working name `spaarke-ai-target-architecture-r1`; hot-path BFF=Y SpaarkeAi=Y; FULL rigor). Also pending: portfolio registration type for the audit project; Action Engine R1 re-based spec. |
| **Decision state (final)** | OQ-1 loop-as-dispatcher (no classifier stack; dispatcher TL→DEL at P2) · OQ-2 engine frozen, coded workflows for new composites, no maker graphs ever · OQ-3 loop-native elicitation · OQ-4/D10 native handlers w/ GA-MCP contracts + OBO spike · E-1/2/3 accepted, E-4/5 rejected · TL = sequencing+rebuild-cost only, HARD cutovers (customer continuity explicitly not a constraint) · ADR-037+ADR-013 amendments APPLIED · ADR-039/040 Proposed (Accepted at P1/P0). |
| **Artifacts (all in `projects/spaarke-ai-code-audit-r1/` unless noted)** | `SPAARKE-AI-CODE-INVENTORY.md` v1.0 · `GREENFIELD-CONCEPTUAL-DESIGN.md` v0.2 (incl. §9 Q&A) · `OVERLAY-MATRIX.md` v1.0 (approved) · `ADR-REVIEW-VS-GREENFIELD.md` v1.1 (approved) · `SPAARKE-AI-MIGRATION-MAP.md` v1.0 · canonical doc v0.4 (`docs/architecture/`) · ADR-039/040 (`.claude/adr/` + `docs/adr/`) · notes/ (7 auditor reports + MCP research + engine-projects synthesis + worktree scan). |

---

## What the audit found (headlines — full detail in the inventory)

1. **Dispatch drift is 10 mechanisms, not 4** — nine live in master's chat path (CompoundIntentDetector,
   PlaybookDispatcher 2-stage, LLM tool loop, SoftSlashRouter intentHint, AgentServiceRoutingMiddleware,
   IntentRerankerService, PlaybookCandidateSelector, ConsumerRoutingService, InvokePlaybookHandler)
   + r7's unmerged regex as #10. None reads session-graph state.
2. **Playbook routing truth split across 4 config surfaces** (sprk_playbookconsumer table, LinearConsumers
   appsettings, Workspace.*PlaybookId appsettings, Insights.Playbooks.Map) + consumer-count disagreement
   across doc(7)/seed(6-7)/ConsumerTypes.cs(8).
3. **~24 duplicate/overlap pairs** (two orchestration engines, dual summarize paths ×3 levels, 3 cross-pane
   mechanisms, 3 duplicated chat hooks, 2 client summarize impls + Compose's third orchestrator, ...).
4. **Dead-code register**: DirectOpenAiAgent/ISprkAgent cluster, ~14-file SpaarkeAi Insights renderer
   cluster, 5 dead PCF dirs, R1 registries/providers, Pillar-6b affordance trio, legacy Chat/Tools.
5. **Manifest docs largely stale** — live R7 vocabulary = executorMetadata.ts (33 executors) +
   R7-refreshed guides + sprk-playbookconsumer.md; 2026-02 ERD docs actively misleading; scope catalog
   4 months stale in 2 divergent copies; multinode playbook blocked on sprk_nodetype option-set gap.
6. **Target-model presence** (Appendix B): M6 widgets mostly exist; session store exists but no
   addressable outputs (M1 gap); Layer 0 / slot-fill (M5) / L4 refusal / runtime Dataverse MCP tools
   absent; Tool framework (typed handlers + sprk_analysistool) is the Tool-catalog embryo.
7. **Worktrees**: 17/24 fully merged — the debt lives in master. Only r7 has a substantive AI delta
   (keep/retire verified in notes/agent-findings-r7-delta.md, incl. 4 items the prior handoff missed:
   debug log, no-revert-path for NL branch, ADR-028 bare-fetch divergence, empty-attachments guard).

## Artifacts written this session (all in `projects/spaarke-ai-code-audit-r1/`)

- `SPAARKE-AI-CODE-INVENTORY.md` — **the Step 1 deliverable** (categories §1-7, overlap register §8, dead-code register §9, caveats §10, worktree appendix A, target scorecard appendix B)
- `README.md`, `spec.md` — project charter + classification rubric/FRs
- `notes/worktree-delta-scan.md` — 24-worktree pre-scan
- `notes/agent-findings-{r7-delta,bff-chat,bff-orchestration,spaarkeai-codepage,client-shared,manifest-schema,peripheral}.md` — 7 auditor reports

## Constraints for next session

- Audit is READ-ONLY — no code/schema/deploy changes were made; none should be made until Step 3 dispositions are approved.
- Do NOT ship more tactical dispatch patches before §7 lands (standing rule from 2026-07-04 pivot).
- Design principles locked in the canonical doc v0.2.6 (D1-D6, three write-shapes, Layer 0 default, two catalogs) — don't re-litigate.
- Environment: `spaarkedev1` Dataverse; `spaarke-bff-dev` App Service — unchanged this session.

## Prior session context (Wave 12.3 + doc history)

Preserved in git history of this file (commit `997c3d717` version) and in
`projects/spaarke-ai-platform-unification-r7/notes/summarize-flow-2026-07-03.md` +
`notes/r7-close-plan-2026-07-03.md`. The Wave 12.3 keep/retire framing is now superseded by the
audit's verified version in `projects/spaarke-ai-code-audit-r1/notes/agent-findings-r7-delta.md`.
