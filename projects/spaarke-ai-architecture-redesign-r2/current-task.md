# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-09 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Protocol: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Progress** | **15 of 53 tasks complete** — the entire FOUNDATION is done |
| **Done** | Phase 0 (001–004), all A0 contracts (010–016), test-repair (021), triple-twin hoist (020), D-F0 doctrine (030), D-F0 eval family (031) |
| **Status** | between tasks; working tree CLEAN (all committed through `fd20e0abe`) |
| **Branch** | `work/spaarke-ai-architecture-redesign-r2` — **0/0 with master** (all merged via PR #586, 2026-07-09). Master @ `4148bb8ec`. |
| **Next Action** | **Task 032 — Policy v2 gate engine** (opus, serial, dep 020✅). Dispatch a subagent running `task-execute` for `tasks/032-confirmation-policy-v2-gate-engine.poml`; implement per `notes/policy-v2-origin-classification-decision-tree.md` (tier table + fail-closed origin classifier + E-1..E-6 + GateDecision v2 producer + association picker). Then 033/034 + the parallel-safe J batch. |

### Immediate next tasks (per TASK-INDEX Parallel Groups)
- **032** Confirmation Policy v2 gate engine (opus, parallel-safe=false, dep 020✅) — implements the GateDecision v2 PRODUCER + tier table + origin classifier + E-1..E-6 (per `notes/policy-v2-origin-classification-decision-tree.md`). Unblocks Compose FR-05 (association-picker live behavior) + 033/034.
- Then serial J-wave: 034 (gate pre-suspend), 035 (Completion Engine, dep 011✅), 036 (job-aware, dep 014✅), 038 (trace view+server read, dep 013✅), 043 (ADR-041).
- Parallel-safe J tasks (can batch): 037 UI-ack, 039 progressive render, 040 refusal-affordance, 041 capability-discovery, 042 create-matter (dep 020✅). 033 origin-eval (dep 032).
- Memory wave M: 050 (dep 016✅) → 051/052; 053 Binder (dep 015✅) → 054 (dep 002✅+053); 055/056/057/060–065.

### Critical Context (essential for continuation)
- **Execution model**: dispatch each task as a subagent running `task-execute` at its POML `<model-tier>` (sonnet default; opus/fable for the flagged tasks). Parallel-safe tasks → concurrent subagents (each creates NEW files, does NOT edit TASK-INDEX/current-task/SEAM-STATUS — main session consolidates + flips rows + commits). parallel-safe=false → run one at a time. Build-verify between waves.
- **Compose r2 coordination**: `notes/SEAM-STATUS.md` is the live dashboard Compose polls. Done seams: 010/011/012/013/014/016 contracts + 020 hoist. Pending for full unblock: 032 (gate engine), 037 (UI-ack), 038 (trace view), 057 (memory.write), 017 (milestone). Flip a seam's SEAM-STATUS row when its task lands. Task 017 posts the "Compose UNBLOCKED" milestone (after 020/037 + contracts).
- **🔔 OPEN escalation (task 001, row 4)**: r1 did NOT retire legacy workspace tools — proceed with the already-scoped **task 075** (no scope change; operator aware).

---

## Full State (Detailed)

### Key decisions this session (also in CLAUDE.md Decisions log)
1. **Memory posture** — `memory.write` is AI-initiated + silent + provenance-tagged (NO gate, no explicit-only floor). Automatic memory = the value prop. Controls: provenance (`source`/`bindingId`/`trustLevel`) + user review/delete + content-safety + scope-isolation. Hard-governance rules (untrusted-origin ban, poisoning evals, semantic boundary, litigation-hold) DEFERRED to a separate governance project. (spec FR-B-08.)
2. **Memory model** — two scopes: Record `(entityType,entityId)` (generalizes MatterMemoryService off sprk_matter) + User (`userId`). NEW Cosmos container partitioned by SUBJECT (not `/tenantId`; dedicated-per-customer envs). Insights-Engine-as-consumer named (wiring = follow-on).
3. **Tool-description source of truth (task 020)** — Model 1 GitOps: seed JSON `sprk_analysistool-*-row.json` = authored source; live Dataverse field = managed mirror (read-only/managed). Code Metadata = Option C validate-only (parity contract test). Future catalog rows (057 memory.*, Compose's 5) author THROUGH the JSON.
4. **Compose seams** — core keeps full seam set; 4 deltas folded (010 supersession, 014 consumer-declared steps, 012/032 association picker, 038 embeddable). AnchoredAnnotation accepted as NOT a MemoryItem.

### Findings downstream tasks MUST use
- **Task 054 (budgets)**: measured baseline (task 002) — Environment ~111 (est ≤50), Business ~1,118 (est ≤1,200, near ceiling; two unconditional directives untracked), Conversation **structurally unbounded to ~8,000** (ledger-outputs path outside the budget tracker). Budgets need real bounding, not rubber-stamp. See `notes/prompt-assembly-baseline.md`.
- **Task 053 (Binder)**: the real per-turn seam is `PlaybookChatContextProvider.GetContextAsync` + `SprkChatAgentFactory` suffix appends + `ChatHistoryManager.BuildLedgerOutputsContext` — NOT `OrchestratorPromptBuilder` (dead code, no prod call site).
- **Task 014/036**: `JobStatusService.cs` is at `Services/Office/`, not `Services/Jobs/`.
- **Task 003**: Business slice is deterministic — stays in the ContextEnvelope stable prefix (NFR-04 intact).
- **Task 064**: r1 already shipped `SessionLedger.CapInlinePayload` (task 001) → 064 is a documented no-op. **Task 075/076**: contingent rows resolved (075 = retire legacy workspace tools; 076 = verify `spaarke-playbook-embeddings` index residual).

### Commits this session (branch, latest first)
`fd20e0abe` 031 eval family · `574160eb3` 030 D-F0 · `78073ae03` 020 hoist · (on master via PR #583: `09acb6218` P0 · `4159a37b1` A0 010-015 · `79ddba20f` 016+021)

### Files/tests added this session (production surface)
- `Services/Ai/PublicContracts/{ComposeDisposition,OutcomeCard,GateDecisionV2,TraceEvent,JobAwareCompletionState,ContextEnvelope,MemoryItem}.cs` + contract tests
- `Services/Ai/Chat/SprkChatAgentFactory.cs` (ResourcefulnessDoctrineDirective; + 020 doc note)
- `Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs` (FindDescriptionParityError) + `RoutingConsumerTypeHealthCheck.cs` (parity dimension) + 23 handler `Metadata.Description` reconciled + `CatalogToolDescriptionParityContractTests.cs`
- `tests/integration/contract/Eval/{resourcefulness-eval-family.json,ResourcefulnessFabricationOracle.cs,ResourcefulnessEvalSuiteTests.cs}`
- `src/client/shared/Spaarke.AI.Widgets/**` (021 repairs + EntityInfoWidget date fix) + jest mocks

### Verification status
Build 0 errors; golden-utterance eval + resourcefulness gate **49/49 green**; publish ~45–48 MB (under 60). Two pre-existing parallel-run flakes (AuditLogServiceTests, SseStreamingIntegrationTests) — both pass in isolation, orthogonal to this work.

---

*Generated by context-handoff 2026-07-09. Resume: "continue" or "where was I?".*
