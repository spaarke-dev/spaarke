# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-10 (post-050, pre-compact — by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Protocol: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Progress (STALE — see freeze row + ledger below)** | **59 of 62 tasks** — ALL implementation DONE + MERGED (PRs #620 #622 #623 #624; masters synced; 079 gate PASS `notes/079-g-r2-d-gate-record.md`). Remaining: 049 + 069 (OPERATOR UAT gates) + 090 wrap-up (test-diet running as agent; final close after operator gates). HELD under deploy freeze: 3 live sprk_analysistool row deactivations (ids in notes/075 report — MUST precede next BFF deploy) + audit copy-forward (optional) + any deploy. Old row follows: 49 of 62 tasks — M2 COMPLETE + **MERGED TO MASTER (PR #620, `866bba817`) + DEPLOYED to spaarke-bff-dev** (hash-verified; memory-items container created on spaarkedev1; memory.write row seeded `2172b721`); M3 (054+056) agents complete, consolidating now. Deferrals filed as GitHub issues #612–#619; env defect #621 (session-cleanup 500, pre-existing) filed. |
| **Task** | 054 ✅ (binding EnvelopeBudget + breach-fails-eval gate 8/8 in GoldenUtteranceEval; reconciliation note) + 056 ✅ (AggregateFreshnessPolicy, deterministic ledger-side; point-lookup byte-identical). Cross-compatible (054's final run included 056's edits: 8120/0). |
| **Status** | M3 MERGED (PR #622). 065 ADR-042 DONE (✅, PR #623 docs-only in flight). **Progress 50/62.** Deployed stack current: BFF from master post-#620, memory-items container live, memory.write seeded. Operator UAT window open (record-bound chat via ribbon EntityFormLaunch or ?entityType=&entityId= URL params; memory review/delete API-only). Known env defect #621 (session-cleanup 500, PRE-existing) fails the AI-surface smoke on every PR — adjudicated per PR, root-cause queued. |
| **⛔ DEPLOY FREEZE** | Operator directive 2026-07-10 (~15:00Z): NO dev-system mutations for ONE HOUR — no BFF deploy, no SpaarkeAi redeploy, NO live Dataverse mutations (incl. the 075 live-row deactivation — HELD), no App Service changes. Git commits/PRs/merges to master are fine (they don't touch the running system). Lift after ~16:00Z. |
| **Next Action** | **TRANCHE H dispatched** (parallel agents): 070 publish-size harness, 071 eval merge gate, 072 seam-fork verify, 073 Track-B hygiene, 076 orphan verify. THEN serial: 074 audit re-key (careful — coordinate 050 subject-key discipline) + 075 legacy workspace tools verdict. Then 079 gate → 090 wrap-up (/test-diet BINDING; deferral pass VERIFIES #612–#619, already filed). Merge #623 when CI passes. UAT findings intake → M4 close (069). |

### Critical Context (essential)
- **Operator rulings 2026-07-09/10 (ALL EXECUTED into POMLs/spec — commits `a28206cfc`, `a70ab868b`)**: per-fact docs aligned MemoryItem v1; fresh container `memory-items` (/subjectId) NO migration (legacy `memory` container SHARED with pins+workspace-tabs — never retire); canonical userId = Dataverse systemuserid; upsert-by-(Type,Key) supersession; **053 converges the INTERACTIVE chat path onto the Binder — do not defer** (audit: 0/6 primitives folded; Binder dispatch-only; Business producer NET-NEW; OrchestratorPromptBuilder DEAD); **governance = MINIMAL** (052 rescoped: review/delete + record-auth read + erasure + retentionClass→Cosmos ttl only; sensitivity/deletionPolicy INERT); **cache stability = keep free determinism, NO cache-key machinery**; **NO mid-wave gate — continuous progression to 069 full-solution feedback**.
- **050 shipped**: `Services/Ai/Memory/{IMemoryItemStore,MemoryItemStore,MemoryItemDocument}.cs` + `DataverseFieldMirrorGuard`; matter-only service DELETED; `PlaybookChatContextProvider.AppendRecordMemoryAsync` generalized (ANY record host reads memory; FR-45 suite retargeted + generalization test); DI in AiPersistenceModule; bicep `memory-items` (defaultTtl -1). Decision record: `notes/050-memory-migration-decision.md`. **Dev deploy note: `memory-items` container must be CREATED on spaarke-ai db before first live write (069 checklist).**
- **051 SHRANK**: 050's `MemoryItemDocument` already carries the FULL 14-field envelope. 051 reduces to: tolerant-reader defaults test (deserialize doc missing new fields → no error), documented field mapping, inert `sourceTrustLevel` negative test, construction test for `source: insights-engine`. ~0.5 day main-session.
- **062 SHRANK**: chips half EXISTS (Binding.ChipTransitions + 035 OutcomeCard chips) — verify + record-precursor items only. **064 = documented no-op** (r1 shipped CapInlinePayload; task 001 verified).
- **Fable session** (operator switched for reasoning quality). Execution model: subagents via task-execute at POML tier; main session consolidates + flips TASK-INDEX + commits; sub-agents can't write `.claude/`.

---

## ⏭️ TRANCHE PLAN to 100% (operator-directed 2026-07-10: "keep progressing, we're behind")

### TRANCHE M2 — memory core completion (IMMEDIATELY after /compact)
1. **051** (envelope tolerant-reader — MAIN session, short; see SHRANK note above; dep 050 ✅).
2. **Parallel agent batch (ONE message, 5 task-execute agents)** — disjoint surfaces, all deps satisfied after 051:
   - **052** minimal governance (Api/Memory endpoint + record-auth read [structural: derive from the caller's own OBO record read, NO parallel ACL] + retentionClass→ttl map + AuditLogService events; sensitivity/deletionPolicy INERT) — opus.
   - **057** memory.write typed tool (AI-initiated SILENT via REAL 044 gate path [low tier + reversible=true catalog data]; provenance envelope; catalog row THROUGH task-020 JSON source mirror-first; CAPTURE+RECALL eval case; upsert-by-key test; live seed deferred to 069) — opus. *dep 051 → dispatch in same batch AFTER 051 commits.*
   - **062** precursors (VERIFY existing chips vs FR-A1-06; record precursor items via store) — sonnet.
   - **064** ADR-040 size-cap documented no-op close — sonnet.
   - **017** seam-publication ordering + cross-project obligation filing — sonnet.
3. Consolidate batch (build+test, flip rows, commit).
4. **053** Binder convergence — **MAIN SESSION, opus·xhigh, the big one**. POML rewritten with real anchors (OrchestratorPromptBuilder is DEAD — delete it as part of fold; real seam = PlaybookChatContextProvider.GetContextAsync + AppendEntityEnrichmentAsync ~602 + SprkChatAgentFactory.CreateAgentAsync ~881 + ChatHistoryManager.BuildLedgerOutputsContext:302 ← ChatEndpoints.cs:615 + gate-outcome ChatEndpoints.cs:2244/2269). PHASED per operator ruling: (1) Binder feature-complete — slice producers incl. NET-NEW Business producer (schema card/write contracts/lookup metadata live as prose in tool descriptions today) + envelope→prompt renderer (deterministic, NO cache machinery) — proven at parity on dispatch; (2) interactive cutover with prompt-regression pinning (byte-identical or recorded deliberate diff); remove folded call sites grep-verified. Memory slices read the 050 store. Wire 061's SemanticScopeProvider CONDITIONALLY here if the retrieval-trigger design lands naturally (PE-D6), else leave deferred.

### TRANCHE M3 — after 053
- **054** budgets (dep 002+053): reconcile vs `notes/prompt-assembly-baseline.md` (Environment 111>50; Business 1,118≈1,200 w/ 2 untracked directives; Conversation structurally ~8k unbounded — must wire under tracker); breach-fails-eval into merge gate. Opus, main or agent.
- **056** fresh-retrieval bias (dep 053; LEDGER-SIDE determinism per POML guidance) — sonnet agent.
- Consolidate → commit → **merge master in → /push-to-github → /merge-to-master** (memory wave lands on master).

### TRANCHE M4 — G-R2-B governance close
- **065** ADR-042 authoring — **Fable MAIN session** (.claude write boundary; prompt line fixed 2026-07-09 — codify AI-INITIATED no-gate posture, NOT explicit-only). Cite what actually shipped (050-057).
- **069 prep**: deploy checklist — BFF from master; CREATE `memory-items` container on spaarkedev1 cosmos (spaarke-ai db, /subjectId, ttl -1); seed memory.write Action/Binding/consumerType (049-style 7-step); SpaarkeAi redeploy if client touched. → **operator runs 069 UAT** (+ 049 still pending operator).

### TRANCHE H — hardening (parallel agents; after M3 merge)
- Parallel: **070** publish-size harness, **071** eval merge gate (deps ✅), **072** seam-fork verify, **073** Track-B hygiene, **076** orphan verify — sonnet agents.
- Serial-ish: **074** audit re-key (coord 050's subject-key discipline; careful), **075** legacy workspace tools verdict (dep 001 ruling: proceed as scoped).
- **079** gate: CI + publish-size + seam-fork verification.

### CLOSE
- **090** wrap-up: `/test-diet` (BINDING) + `/defer` batch filing (PE-D1..D7 formal GitHub issues, two-write rule) + doc-drift-audit + final merge. Operator decisions still parked: PE-D4 (Fork-C facade), PE-D5 (HIGH security project — HELD).

**Estimated: M2+M3 ≈ 2 working sessions; M4 ≈ 1; H ≈ 1 (parallel); close ≈ 0.5. Tail is lighter than row count — 051/062/064 all shrank.**

---

## Session ledger (2026-07-10)
- **Fable architecture review of the memory wave** (2 code audits) → operator ruled Q1-Q4 + round-2 (minimal governance / no cache machinery / no mid-wave gate) → POMLs 050/052/053/056/057/062/065 + spec FR-B-03/04/09/16 amended (`a28206cfc`, `a70ab868b`). Confidence assessment delivered (north-star: exceed Harvey/Legora on governed record-scoped memory; gaps closed in-wave: upsert-by-key hygiene + capture-reliability eval; named follow-on: semantic selection at scale).
- **050 EXECUTED + COMMITTED `3cd5cc6a4`** (FULL rigor; inline 9.5 gates clean; 8060/0; 46.58 MB compressed).
- **Master merged INTO worktree** (`bdf23f11e` — daily-briefing r5 touched PlaybookChatContextProvider/AiPersistenceModule; clean auto-merge; post-merge builds 0 err + 148/148 overlapping suites). Worktree is deploy-safe vs other projects' BFF work.

## Verification status
BFF + tests build 0 errors; full suite **8060 passed / 0 failed** (101 standing skips); publish **46.58 MB compressed** (baseline 49.63, ceiling 60); no new packages/CVEs.

---

*Generated by context-handoff 2026-07-10. Resume: "continue" → start TRANCHE M2 (051 main-session first).*
