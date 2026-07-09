# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-09 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Protocol: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Progress** | **21 of 53 tasks complete** — foundation + Wave J (gate engine + 5 parallel surfaces) done |
| **Done** | Phase 0 (001–004), all A0 contracts (010–016), test-repair (021), triple-twin hoist (020), D-F0 doctrine (030), D-F0 eval family (031), **Wave J parallel batch: 032 gate engine, 037 UI-ack, 039 progressive render, 040 refusal-affordance, 041 capability-discovery, 042 create-matter (2026-07-09)** |
| **Status** | between waves; Wave J being committed. Combined tree VERIFIED GREEN: build 0 errors (3 projects), no conflict markers, eval-gate + gate engine + all new suites **521 passed / 0 failed / 1 pre-existing skip**, publish **48.30 MB** compressed (under 60 ceiling, ~baseline). |
| **Branch** | `work/spaarke-ai-architecture-redesign-r2` — ahead of master by prior checkpoint commits + Wave J commit (this commit). NOT yet merged. |
| **Next Action** | **Serial J-wave (shared gate/factory/completion stack — run ONE AT A TIME in the main session or one subagent at a time):** 033 (origin-classification eval family, sonnet, dep 032✅) → 034 (gate pre-suspend validation, opus, dep 032✅) → 035 (Completion Engine + OutcomeCard, opus, dep 011✅) → 036 (job-aware completion, opus, dep 014✅) → 038 (trace view + server read, opus, dep 013✅) → 043 (ADR-041 authoring, fable, dep 030✅/032✅/035). Then the memory wave M. |

### Immediate next tasks (per TASK-INDEX Parallel Groups — J-serial share the gate/factory/completion files, NOT parallel-safe)
- **033** Origin-classification eval family (sonnet, dep 032✅) — eval cases over the RequestOriginClassifier; pairs with 032's E-1..E-6.
- **034** Gate pre-suspend validation (opus, dep 032✅) — wires the live gate call-site origin/proposal signals (the 032 engine currently defaults origin to `Inferred`/always-suspend until 034 feeds it).
- **035** Completion Engine + OutcomeCard all paths (opus, dep 011✅) — also unblocks DEF-001 (OutcomeCard-structured refusal follow-up).
- **036** Job-aware completion / ingestion-parity (opus, dep 014✅). Note: `JobStatusService.cs` is at `Services/Office/`, not `Services/Jobs/`.
- **038** Traceability view + narration + server read surface (opus, dep 013✅) — publishes the last-but-one seam (TraceEvent view half).
- **043** ADR-041 authoring (fable, dep 030✅/032✅/035) — §6.5 + `.claude/` write-boundary (main-session-only).
- **Memory wave M** (after J-serial): 050 (dep 016✅) → 051/052; 053 Binder (dep 015✅) → 054 (dep 002✅+053); 055/056/**057 memory.write**/060–065. 057 publishes the final seam; **017** milestone posts "Compose UNBLOCKED" after 038+057.

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
