# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-09 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Protocol: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Progress** | **31 of 53 tasks + Phase E started** — G-R2-A DEPLOYED to spaarkedev1 + UAT-READY; Phase E (AI Execution Foundation) E-00/E-10 done |
| **Done** | Phase 0 (001–004), A0 contracts (010–016), 021, 020, D-F0 (030/031), Wave J (032/037/039/040/041/042), Wave K (033/034/035/036/038), 043 ADR-041, **044 live gate + 044c action_outcome client render**, 049 UAT script; **Phase E: E-00 ADR-043, E-10 ContextBinder+ActionRunner** (all on master via PRs #595/#596/#598/#600) |
| **Status** | **G-R2-A FULLY DEPLOYED to spaarkedev1** (BFF spaarke-bff-dev verified live Wave J+K+044; SpaarkeAi code page `sprk_spaarkeai` deployed 2026-07-09 with 044c fix — action_outcome verified in bundle). All merged to master (PR #600, master @ `5c4727ccd`); worktree coordinated with 24 other-project commits. |
| **Branch** | `work/spaarke-ai-architecture-redesign-r2` — all work merged to master via PR #600. Clean. |
| **Next Action** | **AWAITING OPERATOR: run the G-R2-A 10-scenario browser UAT** (`notes/g-r2-a-uat-script.md`) on spaarkedev1 → full pass promotes ADR-041 Proposed→Accepted, closes G-R2-A. (Optional pre-step: seed cataloged create-matter DEF-003 — not required, generic path works.) **Then continue Phase E: E-12 (converge node engine + retire narrator ## Input replica), E-20 (single-source disposition), E-30 (deterministic ActionKind/supersession) — see `notes/ai-execution-foundation-remediation-plan.md`. Then memory wave M (050→053 Binder→054, 057 memory.write, 065 ADR-042).** |

### 🔔 Phase E — AI Execution Foundation (NEW, operator-directed "fix fully in r2")
- **Why**: the canonical execution engine realized only a narrow slice of the ADR-039 catalog contract (input=files-only, disposition=2-of-6, kind=Prompted-only; two redundant completion engines, canonical the weaker). Best-architecture fix, NOT consumer-unblock (memory `architecture-over-feature-unblocking`).
- **Plan**: `notes/ai-execution-foundation-remediation-plan.md` + ADR-043. Moves: 1 input-resolution (E-10 ✅), 2 single-source disposition (E-20), 3 deterministic ActionKind (E-30), engine convergence (E-12), governance seam/** KEEP test.
- **Done**: E-00 (ADR-043 Proposed), E-10 (ContextBinder + single-source ## Input + wire ActionRunner; designed against all 3 consumers; found+fixed OutputRouter fingerprint-clobber bug).
- **Coordination**: compose-r2 (B1-B6, they build against ContextEnvelope; B5→core) + daily-briefing (E-12 retires their ## Input replica) handoffs delivered. Reserves the future multi-step "Action Engine" seam (hybrid auth / closed-catalog / ledger plan / framework-agnostic).

### 🔔 G-R2-A gate + open items for operator
- **ConfirmationPolicyEngine 0-core-call-sites — RESOLVED 2026-07-09 by task 044** (operator ruled (b) wire-up; re-scoped to risk+ambiguity after escalation). Engine now LIVE on the core gate. Model: agent asks when torn (layer 1); gate confirms only high-risk/irreversible + optional dispatchUncertain backstop; clear+complete low-risk → execute+Undo no-dialog; email = draft + review/send deep link (NO auto-send in r2, deferred). Confirmation model captured in memory `confirmation-model-ambiguity-not-origin`.
- **NEXT to close G-R2-A**: (1) DEPLOY G-R2-A code (Wave J+K+044) + create-matter catalog seed (DEF-003/#593) to spaarkedev1; (2) author task 049 UAT script; (3) **operator runs the 10-scenario browser UAT on spaarkedev1** — pass flips ADR-041 Proposed→Accepted. Cannot be auto-run.
- **Task 053 (Binder)** is the intended writer for task 038's dark-landed `SessionContextFingerprint` seam (`AppendContextFingerprintAsync`) — sequence 053 to wire it.

### 🔔 OPEN ITEM (surfaced by task 034, needs operator decision) — ConfirmationPolicyEngine has 0 core call-sites
- Task 032 shipped `ConfirmationPolicyEngine` (Policy v2 PRODUCER, tiers + E-1..E-6) as a published seam. But it is **DARK in the core**: 034 = pre-suspend `ValidateChat` (per authoritative spec FR-A1-05 — NOT engine wiring), and 042 reused the existing gated `dataverse.create_record` path. **No current WBS task wires the engine into the core's own live gate.** Correct for Compose r2 (it consumes the engine directly), but the core's live gate still runs on the pre-existing suspend floor + 034's pre-suspend validation. **Decision needed:** (a) accept as-is (engine is a Compose-consumed seam, core doesn't need it live), (b) add a small task to wire it at the Binding-dispatch/resume surface, or (c) documented deferral. Flagged to operator 2026-07-09.

### Immediate next tasks
- **035** Completion Engine + OutcomeCard all paths (opus, dep 011✅) — clears DEF-001; consume `JobAwareOutcomeProjection` for async paths.
- **038** Traceability view + narration + server read surface (opus, dep 013✅) — publishes the TraceEvent view seam (2nd-to-last seam).
- **043** ADR-041 authoring (fable, dep 030✅/032✅/035) — §6.5 + `.claude/` write-boundary (main-session-only, NOT a subagent).
- **Memory wave M** (after J-wave): 050 (dep 016✅) → 051/052; 053 Binder (dep 015✅) → 054 (dep 002✅+053); 055/056/**057 memory.write**/060–065. 057 publishes the final seam; **017** milestone posts "Compose UNBLOCKED" after 038+057.

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
