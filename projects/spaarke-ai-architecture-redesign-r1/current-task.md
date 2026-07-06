# Current Task State — spaarke-ai-architecture-redesign-r1

> **Last Updated**: 2026-07-06 ~02:00 (P1 closed; W-P2-A dispatching — operator asleep, autonomous overnight run per operator directive)
> **Recovery**: Read "Quick Recovery" first. Full context: this file + tasks/TASK-INDEX.md + plan.md + notes/g-p1-uat-round2-findings.md.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 034 — HARD CUTOVER chat NL → agent loop (serial, parallel-safe FALSE) — dispatching |
| **Step** | W-P2-A + W-P2-B COMPLETE (030/031/032/033 ✅). P1 closed earlier tonight. |
| **Status** | autonomous overnight execution (operator directive 2026-07-06: "move on to P2 while i sleep") |
| **Next Action** | On 034's return: flip TASK-INDEX, build + suite triage vs KNOWN list, commit (`git pull --rebase` first), push, portfolio sync, then W-P2-D (035 + 036 parallel deletions) → 037 (eval + injection). STOP at gate 038 (operator browser UAT — NEVER auto-pass, NFR-11). |

### P1 close summary (2026-07-06)
- G-P1 ran TWO UAT rounds. Round-2 findings + fixes: `notes/g-p1-uat-round2-findings.md` — RD-1 chip strip stranded at top → SprkChat `aboveInputSlot` prop, chips now above input zone; RD-2 "Summarize again" frozen to original fileIds → transition chips carry NO args, dispatch-time FR-08 default-all; RD-3 latent composer-strip gating → session-level count (promotedChipIds ∪ composer-ready).
- Fix commit `9ee30e672`; BFF redeployed (healthz 200) + sprk_spaarkeai republished (4781 KB, id 5206a442-3451-f111-bec7-7ced8d1dc988).
- Legacy "Open Library" no-match reply on typed NL is EXPECTED until task 034 — verify gone at gate 038.
- 027 close is operator-directed conditional pass: chip-placement visual spot-check folds into gate 038.

### Session commits (all pushed; CI bot pushes format commits — ALWAYS `git pull --rebase` before push)
`c061c621b` init · `091ed29d6` budget · `1aa317b35` W-P0-A · `ef771def1` W-P0-B · `a93bd6dce` W-P0-C · `70d599348` gate 014 · `bdfcb06ba` W-P1-A · `a34bb877a` 021 · `6f7622f3c` W-P1-C · `d1d2a1c06` 023b · `9a4270a5b` 022b+026+ADR-039 · `befcaa5da` round-1 fixes · `efef0c398` handoff · `9ee30e672` round-2 fixes

### Critical Context
- **Progress**: 27/51 ✅ (P0 + P1 complete; Track-B 4/4). Portfolio Issue #550 (Epic #421); Tasks Completed sync to 27 (GraphQL: project PVT_kwHODW0Pv84BEgWu, item PVTI_lAHODW0Pv84BEgWuzgxza1E, field PVTF_lAHODW0Pv84BEgWuzhWPlLY). Draft PR #551.
- **ADR-039 + ADR-040 ACCEPTED** — binding for all remaining phases.
- **Known pre-existing test failures (NOT ours)**: ExecutorConfigSchemas, KnowledgeDeploymentConfig, DailyBriefingCollector resolver, TemplateContextBuilder TextOnly, SessionFilesCleanup (5) + AuditLogService & PlaybookDispatcherPhaseB latency flakes under parallel load (pass in isolation) + NetArchTest 5 (ADR-010 ceiling 129 vs 76).
- **Task 030 extra acceptance criterion**: orphan-handler health dimension Degraded→Unhealthy escalation (14 orphan handlers currently Degraded-by-design on /healthz/catalog).
- **F-1 ruling**: accept-until-cutover; P2 tasks 035/036 delete most legacy legs; task 044 deletes InvokePlaybookHandler/AnalysisQueryHandler/WorkingDocumentHandler AND must re-home the E-2 EngineOutputLedgerAdapter (040 re-points BindingId=playbookId first).
- **Deferrals on record**: ADR-040 size-cap → 047; ConversationPane batch-state extraction + concurrency-safe manifest append + Task.Delay→TimeProvider (readiness probe) → /defer or task 045; Seed-JpsActions.ps1 sweep needs main session (073); ConversationPane ≤300-line budget at 045 with escalation valve.
- **Operator TODO**: mark eval-gate CI job REQUIRED when branch protection returns.

## Wave protocol (unchanged — keep doing this)
1. Parallel task agents (max 6/wave) via Agent tool: POML path, task-execute protocol, rigor declaration, file-ownership boundaries, known-failures list, no-commit/no-TASK-INDEX/no-.claude boundaries.
2. Stagger intra-wave deps. 3. Wave end: main session flips TASK-INDEX, dotnet build + suite triage, commit/push, portfolio sync. 4. Verify-dead-first on inventory items. 5. `.claude/` edits main-session only. 6. `/goal` conditions in TASK-INDEX wave headers (operator-side pilot).

## P2 sequence (this overnight run)
- W-P2-A: 030 + 031 (parallel) → W-P2-B: 032 + 033 (parallel; 032 deps 030+031, 033 deps 030) → W-P2-C: 034 HARD CUTOVER (serial) → W-P2-D: 035 + 036 (dispatcher-stack + Chat/Tools deletions) → 037 (eval + injection hardening) → **gate 038 STOP** (operator browser UAT: four-outcome contract, session memory, confirmed writes, chip placement spot-check, legacy no-match reply gone).

## Decisions This Session (operator-ratified where noted)
1. Stop-after-init; Target 2026-08-15. 2. ConversationPane ≤300-line budget + valve. 3. F-1 accept-until-cutover. 4. /healthz split; dup-detection keys sprk_toolid; orphans Degraded until 030. 5. ComposeSummarize/ChatClassify constants. 6. OBO mcp.tools spike FAIL-with-path. 7. Upload UX: auto-classify + chip-offered summarize (catalog data). 8. **027 operator-directed close + autonomous P2 overnight (2026-07-06)**.

## W-P2-A outcomes (2026-07-06, both Step 9.5 PASS)
- **030**: budget-8 CAS contract + BudgetedAIFunction; Binding capability-tools projected into the loop via SessionDispatchOrchestrator (opt-in = non-empty sprk_tooldescription; 5-min cache); deterministic pre-filter (sprk_surfaces + structural facts only); NFR-04 SHA-256 projection fingerprint; citation repair block at stream end; ToolChain flushed to ledger BEFORE each rendered segment; orphan handlers now Unhealthy (TemplateHandler deleted — the "14 orphans" figure was stale); SprkChatAgentFactory 2714→1942 lines.
- **031**: PendingPlanManager = THE pending store (PendingInvocation suspend/resume/reject, double-confirm safe); gating = RequiresConfirmation(side_effect_class, risk, dispatchUncertain); CompoundIntentDetector tool-name lists deleted grep-zero; /actions/{id}/confirm endpoint + DTOs + client leg deleted; SessionGate markers via ChatSessionManager.AppendGateAsync; working-doc write-back tool row declares sprk_sideeffectclass=Write (Seed-TypedHandlers re-run DONE on spaarkedev1).
- Integration seams for 032/034 documented in notes/task-030-agent-turn-loop-notes.md + notes/task-031-confirmation-gate-notes.md.
- /healthz/catalog on the DEPLOYED instance stays Degraded until next branch deploy (deleted TemplateHandler still in the running registry) — flips at gate 038 deploy.
- 031 W1 (suspend on missing session skips marker — tighten at W-P2-B) + W2 (extra catalog query on legacy path — dies at 034) + client dead-leg (032 rewires ActionConfirmationDialog as gate presentation).

## W-P2-B outcomes (2026-07-06, both Step 9.5 PASS)
- **032**: elicitation = loop state. BindingCapabilityTool validates against sprk_inputschema → missing args suspend into the ONE pending store with elicitation Gate marker BEFORE render; clarifying turn grounded in declared fields only; capture_mode=modal → `elicitation_modal` SSE → wizard; mid-elicitation utterances route as ANSWERS via ElicitationTurnRouter (hard-slash/restart-phrase escape); resolve at SessionDispatchOrchestrator.ResolveElicitationOnDispatchAsync; unified gate-resolve endpoint POST /sessions/{id}/gates/{gateId}/resolve; client ActionConfirmationDialog rewired to it; 031-W1 now throws. NOTE: no spaarkedev1 row declares required inputs yet — machinery activates with P3 create-task/matter-pre-fill.
- **033**: refusal = fourth loop outcome, pure catalog data. REF-CHAT@v1 Action `8d337be2-3d79-f111-ab0e-7ced8ddc4cc6` + no_match_handler Binding `48dcd7ec-3d79-f111-ab0e-7ced8ddc4cc6` (ucid L4-REFUSAL) on spaarkedev1; RefusalCapabilityTool renders via IActionRunner (file-less-safe) + ledger-before-return + `dispatch_refused` counter (meter Sprk.Bff.Api.Ai); grounded-outcomes system-prompt directive (NFR-04 stable). App Insights LIVE evidence deferred to gate-038 deploy (KQL in notes/task-033-honest-refusal-notes.md). 033 evolved 032's GU-046 eval invariant (clarify MAY name capability under elicitation) — acked by main session in wave commit.
- Combined-tree suite (main session): 8059 — 7952 passed / 6 failed, ALL known (5 pre-existing + PhaseB flake). Eval 12/12.
- 034 integration notes in notes/task-030/031/032/033-*.md: delete PlaybookDispatcher pre-pass + Phase-B + DetectToolCallsAsync blocks; keep isElicitationAnswerTurn effectiveTurnMessage substitution; keep FR-P2-04 directive block; AgentToolCatalogProjector+Finalize = single projection path; route resumed invocations through SessionDispatchOrchestrator.

## Parallel Execution

034 dispatching (serial — parallel-safe FALSE; hard cutover).
