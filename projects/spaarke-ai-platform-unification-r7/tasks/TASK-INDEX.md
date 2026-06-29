# Task Index — Spaarke AI Platform Unification R7

> **Last Updated**: 2026-06-28
> **Source plan**: [`../plan.md`](../plan.md)
> **Source spec**: [`../spec.md`](../spec.md)
> **Total tasks generated**: **82 POML files across 10 waves** (all waves seeded)
> **Generation method**: 10 parallel subagents (one per wave) dispatched by `/project-pipeline` Step 3

## Status Legend

- 🔲 not-started
- 🔄 in-progress
- ✅ completed
- ⏸️ blocked / waiting on dependency
- ❌ abandoned (re-spec required)

## Status Snapshot

| Wave | Goal | Status | Tasks |
|---|---|---|---|
| Wave 1 | AiCompletionNodeExecutor build (FR-12 to FR-15) | 🔄 in-progress | 001-010 ✅ generated (10 files) |
| Wave 2 | Dispatch refactor + enum rename (FR-07 to FR-10) | ⏸️ blocked on Wave 1 | 020-029 ✅ generated (10 files) |
| Wave 3 | Typed config schemas (FR-16) | ⏸️ blocked on Wave 1 | 030-036 ✅ generated (7 files) |
| Wave 4 | Schema cleanup + remove legacy direct-path (FR-03, FR-04, FR-11) | ⏸️ blocked on Wave 2 ONLY (task 040 audit confirmed Wave 9 NOT a prerequisite — SessionSummarizeOrchestrator does NOT call ExecuteAnalysisAsync; only 1 production caller at AnalysisEndpoints.cs:261) | 040 ✅, 041-047 generated (8 files) |
| Wave 5 | Existing-playbook backfill (FR-19, FR-20) | ⏸️ blocked on Wave 2 | 050-056 ✅ generated (7 files) |
| Wave 6 | Documentation deletion + updates (FR-28 to FR-31) | ⏸️ blocked on Wave 2 (partial) | 060-069 ✅ generated (10 files) |
| Wave 7 | Skill rewrites (FR-32, FR-33) | ⏸️ blocked on Wave 2 | 070-075 ✅ generated (6 files) |
| Wave 8 | Playbook Builder UI updates (FR-21 to FR-27) | ⏸️ blocked on Wave 2 + Wave 3 | 080-089d ✅ generated (14 files) |
| Wave 9 | Consumer migration (FR-17, FR-18) | ⏸️ blocked on Wave 2 | 090-096 ✅ generated (7 files) |
| Wave 10 | Wrap-up + R4 graduation gate close | ⏸️ blocked on all waves | 100, 101, 090-project-wrap-up ✅ generated (3 files) |

**Total: 82 POML files generated. Ready for Wave 1 execution.**

---

## Wave 1 — AiCompletionNodeExecutor build (FR-12 to FR-15)

**Goal**: close R4 `/narrate` end-to-end gap. Wave 1 is the foundation for Waves 2-10.
**Estimated**: ~10 tasks, 2-3 days.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 001 | ✅ | Audit existing AiAnalysisNodeExecutor + EntityNameValidatorNodeExecutor for AiCompletion patterns | audit, bff-api | yes | — |
| 002 | ✅ | Scaffold AiCompletionNodeExecutor.cs (interface impl, ctor, Validate skeleton) | bff-api, code-impl | yes | 001 |
| 003 | ✅ | Implement payload binding + PromptSchemaOverrideMerger integration | bff-api, code-impl | yes | 002 |
| 004 | ✅ | Implement IOpenAiClient.GetStructuredCompletionRawAsync call + JsonElement binding | bff-api, code-impl | yes (with 003) | 003 |
| 005 | ✅ | Implement Validate() — Action FK required; Tool/Document NOT required | bff-api, code-impl | yes (with 003) | 002 |
| 006 | ✅ | Register AiCompletionNodeExecutor as Singleton in AnalysisServicesModule | bff-api, di | yes | 003-005 |
| 007 | ✅ | xUnit tests — payload binding + schema rendering + template substitution | bff-api, testing | yes | 003 |
| 008 | (to be generated) | xUnit tests — temperature override + per-node prompt override | bff-api, testing | yes (with 007) | 007 |
| 009 | ✅ | xUnit tests — error paths (missing prompt, malformed JSON, LLM error) | bff-api, testing | yes (with 007) | 007 |
| 010 | (to be generated) | Wave 1 BFF publish + size check (NFR-01) + CVE scan (NFR-02) | bff-api, deploy | yes | 006, 009 |

## Wave 2 — Dispatch refactor + enum rename (FR-07 to FR-10)

**Goal**: collapse 3-layer dispatch to single-hop `node.sprk_executortype` read; rename `ActionType` → `ExecutorType` across BFF.
**Estimated**: ~18 tasks, 3-4 days.
**Critical**: task 022 (enum rename) is a single sequential task — large diff intentionally batched.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 020 | ⏸️ | Audit all `ActionType` references via grep (~1000+) | audit, bff-api | yes | W1 complete |
| 021 | ⏸️ | Plan rename strategy (PR sizing, conflict-risk windows) | planning | yes | 020 |
| 022 | ⏸️ | Rename C# enum ActionType → ExecutorType (full refactor, BFF only) | bff-api, code-impl, refactor | no (single large diff) | 021 |
| 023 | ⏸️ | Rename `INodeExecutor.SupportedActionTypes` → `SupportedExecutorTypes` | bff-api, code-impl | yes | 022 |
| 024 | ⏸️ | Update `PlaybookOrchestrationService.ExecuteNodeAsync` to read single-hop (FR-07) | bff-api, code-impl | yes | 022 |
| 025 | ⏸️ | Delete structural fallback ladder (FR-08, ~150 LOC) | bff-api, code-impl | yes | 024 |
| 026 | ⏸️ | Delete Action ActionType override branch lines 1241-1278 (FR-09) | bff-api, code-impl | yes (with 025) | 024 |
| 027 | ⏸️ | Update `NodeExecutorRegistry` dispatch to use `ExecutorType` | bff-api, code-impl | yes (with 025-026) | 023 |
| 028 | ⏸️ | Update `AnalysisActionService` Action read path | bff-api, code-impl | yes (with 025-027) | 024 |
| 029 | ⏸️ | Wave 2 BFF publish + size check (NFR-01) + CVE scan | bff-api, deploy | yes | 022-028 |

## Wave 3 — Typed config schemas (FR-16)

**Goal**: each `INodeExecutor` declares config schema; BFF serves schemas to PlaybookBuilder.
**Estimated**: ~8 tasks, 2 days. Can start after Wave 1.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 030 | ⏸️ | Design GetConfigSchema() signature + schema DTO shape | bff-api, planning | yes | W1 complete |
| 031 | ⏸️ | Add GetConfigSchema() to INodeExecutor interface | bff-api, code-impl | yes | 030 |
| 032 | ⏸️ | Implement GetConfigSchema() on all 33 executors | bff-api, code-impl | yes | 031 |
| 033 | ⏸️ | Implement BFF endpoint GET /api/ai/playbook-builder/executor-config-schemas | bff-api, code-impl | yes | 032 |
| 034 | ⏸️ | xUnit tests for endpoint + schema serialization | bff-api, testing | yes | 033 |
| 035 | ⏸️ | Document schema shape in `docs/architecture/AI-ARCHITECTURE.md` | docs | yes | 030 |
| 036 | ⏸️ | Wave 3 BFF publish + size check (NFR-01) | bff-api, deploy | yes | 033, 034 |

## Wave 4 — Schema cleanup + remove legacy direct-path (FR-03, FR-04, FR-11)

**Goal**: delete unused columns + legacy `ExecuteAnalysisAsync`.
**Estimated**: ~8 tasks, 1-2 days. **Must follow Wave 9** (chat-summarize migration).

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 040 | ✅ | Audit all callers of AnalysisOrchestrationService.ExecuteAnalysisAsync | audit, bff-api | yes | — |
| 041 | ⏸️ | Migrate non-chat callers to PlaybookOrchestrationService.ExecuteAsync (FR-11) | bff-api, code-impl | yes | 040, 091 (W9 chat-summarize done) |
| 042 | ⏸️ | DELETE ExecuteAnalysisAsync + cascading dead code (FR-11) | bff-api, code-impl, deletion | no | 041 |
| 043 | ⏸️ | Drop sprk_analysisaction.sprk_actiontypeid (lookup) via dataverse-create-schema (FR-03) | dataverse-schema, deletion | no | 042 |
| 044 | ⏸️ | Drop sprk_analysisaction.sprk_executoractiontype (INT) (FR-04) | dataverse-schema, deletion | no (sequential with 043) | 043 |
| 045 | ⏸️ | Document sprk_analysisactiontype as decorative (FR-05) | docs | yes | — |
| 046 | ⏸️ | Update AnalysisActionService to remove references to dropped fields | bff-api, code-impl | yes (after 044) | 044 |
| 047 | ⏸️ | Wave 4 BFF publish + size check (expect SHRINK) | bff-api, deploy | yes | 042-046 |

## Wave 5 — Existing-playbook backfill (FR-19, FR-20)

**Goal**: populate `sprk_executortype` on 94 existing nodes in spaarkedev1; update Deploy-Playbook.ps1.
**Estimated**: ~7 tasks, 2-3 days (includes owner-review checkpoint).
**Sequential within wave** (owner checkpoint at task 052).

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 050 | ⏸️ | Author Review-PlaybookNodes-Dispatch.ps1 (FR-19) | script, dataverse | no (sequential) | 024 (W2 dispatch ready) |
| 051 | ⏸️ | Run review tool; produce CSV for owner review | dataverse, audit | no | 050 |
| 052 | ⏸️ | **OWNER CHECKPOINT** — owner reviews + sets each value (94 nodes) | manual, owner-gate | no | 051 |
| 053 | ⏸️ | Author Migrate-PlaybookNodes-to-ExecutorType.ps1 (idempotent + dry-run) | script, dataverse | no | 052 |
| 054 | ⏸️ | Run migration (dry-run → real run); audit post-migration null check | dataverse, deploy | no | 053 |
| 055 | ⏸️ | Update Deploy-Playbook.ps1 to write executor type explicitly (FR-20) | script | no | 054 |
| 056 | ⏸️ | Sanity — redeploy 3 representative playbooks (Daily Briefing, Insights, chat) | dataverse, deploy | no | 055 |

## Wave 6 — Documentation deletion + updates (FR-28 to FR-31)

**Goal**: DELETE outdated R4 canonical-truth sections, UPDATE current sections, CREATE consumer-wiring guide.
**Estimated**: ~12 tasks, 2-3 days. Can parallelize with Wave 5-8.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 060 | ⏸️ | Audit ai-architecture-playbook-runtime.md for outdated sections | docs, audit | yes | 024 |
| 061 | ⏸️ | DELETE §5 action-lookup precedence ladder + structural-fallback section | docs, deletion | yes (with 062-066) | 060 |
| 062 | ⏸️ | UPDATE ai-architecture-actions-nodes-scopes.md 4-Home decision tree | docs | yes | 024 |
| 063 | ⏸️ | UPDATE ai-guide-playbook-deploy-recipe.md — remove Control-flow name-detection | docs | yes | 055 |
| 064 | ⏸️ | UPDATE `.claude/constraints/bff-extensions.md` §G (FR-29) | docs, skill-directives | no (sequential per Sub-Agent Write Boundary) | 024 |
| 065 | ⏸️ | MAJOR UPDATE JPS-AUTHORING-GUIDE.md (FR-30) | docs | yes | 024 |
| 066 | ⏸️ | MAJOR UPDATE PLAYBOOK-AUTHOR-GUIDE.md (FR-30) | docs | yes | 024 |
| 067 | ⏸️ | CREATE ai-guide-consumer-wiring.md (FR-31) | docs | yes | 091 (W9 chat-summarize done) |
| 068 | ⏸️ | UPDATE root CLAUDE.md if entry-points table affected | docs | no (sequential per Sub-Agent Write Boundary) | 067 |
| 069 | ⏸️ | Post-audit: grep `docs/` for new "deprecated"/"superseded" instances (NFR-08) | docs, audit | yes | 061-068 |

## Wave 7 — Skill rewrites (FR-32, FR-33)

**Goal**: rewrite jps-* skills for node-first dispatch model.
**Estimated**: ~6 tasks, 1-2 days.
**SEQUENTIAL ONLY** per CLAUDE.md §3 Sub-Agent Write Boundary — `.claude/skills/` writes happen only in main session.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 070 | ⏸️ | REWRITE `.claude/skills/jps-action-create/SKILL.md` (FR-32) | skill-directives, docs | no | 024 |
| 071 | ⏸️ | REWRITE `.claude/skills/jps-playbook-design/SKILL.md` (FR-32) | skill-directives, docs | no | 070 |
| 072 | ⏸️ | REWRITE `.claude/skills/jps-playbook-audit/SKILL.md` (FR-32) | skill-directives, docs | no | 071 |
| 073 | ⏸️ | REWRITE `.claude/skills/jps-validate/SKILL.md` (FR-32) | skill-directives, docs | no | 072 |
| 074 | ⏸️ | MINOR UPDATE `.claude/skills/jps-scope-refresh/SKILL.md` (FR-33) | skill-directives, docs | no | 073 |
| 075 | ⏸️ | Run /jps-validate on representative playbooks to confirm functional | testing | no | 074 |

## Wave 8 — Playbook Builder UI updates (FR-21 to FR-27)

**Goal**: replace Node Type with Executor Type Choice + 33-executor categorized selector + typed config forms + Action tab.
**Estimated**: ~16 tasks, 3-4 days.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 080 | ✅ | Audit PlaybookBuilder canvas state for `sprk_nodetype` references | audit, code-page | yes | — |
| 081 | ⏸️ | Replace Node Type field with Executor Type Choice on Power Apps form (FR-21) | dataverse, form-update | yes | 024 |
| 082 | ⏸️ | Update canvas Node Types left panel — 33 categorized entries (FR-22) | code-page, ui | yes | 024 |
| 083 | ⏸️ | Wire typed config form renderer driven by schema endpoint (FR-23) | code-page, ui | yes | 033 (W3 endpoint) |
| 084 | ⏸️ | Implement typed config forms for 5 priority executors (FR-23) | code-page, ui | yes | 083 |
| 085 | ⏸️ | Implement remaining 28 executor schemas (placeholders OK) | code-page, ui | yes | 084 |
| 086 | ⏸️ | Promote Action selection to new Action tab (FR-24) | code-page, ui | yes | 082 |
| 087 | ⏸️ | KEEP Prompt tab + per-node override wiring (FR-25) — UAT | code-page, ui, testing | yes | 086 |
| 088 | ⏸️ | Replace `sprk_nodetype` references in canvas state (FR-26) | code-page, code-impl | yes | 080 |
| 089 | ⏸️ | Handle unknown-executor-type warning state (FR-27) | code-page, ui | yes | 085 |
| 089a | ⏸️ | UI test — Executor Type dropdown + tier grouping render | code-page, testing, ui-test | yes | 089 |
| 089b | ⏸️ | UI test — typed config forms for 5 priority executors | code-page, testing, ui-test | yes | 084 |
| 089c | ⏸️ | UI test — ADR-021 dark mode compliance | code-page, testing, ui-test | yes | 081-088 |
| 089d | ⏸️ | Deploy PlaybookBuilder Code Page to spaarkedev1 | code-page, deploy | yes | 089a-c |

## Wave 9 — Consumer migration (FR-17, FR-18)

**Goal**: migrate chat-summarize + wire Playbook Library into ≥3 consumer surfaces.
**Estimated**: ~7 tasks, 2 days.
**Order matters**: tasks 090-091 (chat-summarize) must precede Wave 4 task 042 (ExecuteAnalysisAsync deletion).

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 090 | ⏸️ | Audit SessionSummarizeOrchestrator caller graph; design Path A.5 migration | audit, bff-api | yes | 024 |
| 091 | ⏸️ | Migrate SessionSummarizeOrchestrator to IConsumerRoutingService + IInvokePlaybookAi (FR-17) | bff-api, code-impl | yes | 090 |
| 092 | ⏸️ | Add chat-summarize row to sprk_playbookconsumer table | dataverse | yes | 091 |
| 093 | ✅ | Audit Playbook Library Code Page modal current routing | audit, code-page | yes | — |
| 094 | ⏸️ | Wire Library modal into spaarke-ai chat surface (FR-18) | code-page, ui | yes | 093 |
| 095 | ⏸️ | Wire Library modal into briefing widget (FR-18) | code-page, ui | yes | 094 |
| 096 | ⏸️ | Wire Library modal into ad-hoc launcher (FR-18) | code-page, ui | yes | 095 |

## Wave 10 — Wrap-up

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 100 | ⏸️ | End-to-end verification of 15 success criteria | testing, audit | no | All waves done |
| 101 | ⏸️ | UAT — /narrate via Daily Briefing widget (R4 graduation gate, FR-15) | uat, testing | no | 100 |
| 090-project-wrap-up | ⏸️ | Wrap-up — README → Complete, lessons-learned.md, archive | wrapup | no | 101 |

---

## Parallel Execution Groups

Tasks within a group can run concurrently (separate `task-execute` invocations in one message). Dependencies between groups enforce ordering.

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| W1-A | 001 | — | Single audit task |
| W1-B | 002 | 001 ✅ | Single scaffold task |
| W1-C | 003, 005 | 002 ✅ | Parallel: implementation + validation skeleton |
| W1-D | 004, 006 | 003 ✅ | Parallel: LLM call + DI registration |
| W1-E | 007, 008, 009 | 003 ✅ | Parallel: 3 test categories |
| W1-F | 010 | 006 + 009 ✅ | Single deploy + size check |
| W2-A | 020, 021 | W1 ✅ | Sequential within (audit → plan) |
| W2-B | 022 | 021 ✅ | **Single large diff** — sequential |
| W2-C | 023, 024 | 022 ✅ | Parallel |
| W2-D | 025, 026, 027, 028 | 024 ✅ | Parallel: 4 cleanup tasks |
| W2-E | 029 | 028 ✅ | Single deploy |
| W3 | 030-036 | W1 ✅ | Independent of W2; can start in parallel with W2 |
| W4 | 040-047 | W9 091 ✅ + W2 ✅ | Sequential within (43-44-46 schema cascade) |
| W5 | 050-056 | W2 024 ✅ | Sequential with owner checkpoint at 052 |
| W6 | 060-069 | W2 024 ✅ (partial) | Parallel except 064 (constraint update) + 068 (root CLAUDE) — SEQUENTIAL per Sub-Agent Write Boundary |
| W7 | 070-075 | W2 024 ✅ | **SEQUENTIAL ONLY** per Sub-Agent Write Boundary |
| W8 | 080-089d | W2 + W3 033 ✅ | Parallel: UI subtasks |
| W9 | 090-096 | W2 024 ✅ | Sequential within (090 → 091 → 092; then 093-096 sequential) |
| W10 | 100, 101, wrap-up | All waves ✅ | Sequential |

### Parallel-safety summary

- **Sub-Agent Write Boundary tasks** (must run sequentially, main session only): 022, 042, 043, 044, 064, 068, 070, 071, 072, 073, 074, 075, 100, 101, 090-project-wrap-up
- **All other tasks**: parallel-safe within their group

---

## Critical Path

The longest dependency chain through the WBS (estimated 12-15 days):

```
001 → 002 → 003 → 006 → 010 (Wave 1 complete)
  → 020 → 021 → 022 (enum rename — single large diff)
  → 024 (dispatch refactor)
  → 050 → 051 → 052 (owner review) → 053 → 054 → 055 → 056 (backfill complete)
  → 089d (PlaybookBuilder UI deployed) — depends on W8 critical path 080 → 082 → 089a-c
  → 100 → 101 → 090-project-wrap-up
```

Other waves run in parallel where dependencies allow.

---

## High-Risk Items

| Task | Risk | Mitigation |
|---|---|---|
| 022 (enum rename) | Large diff blocks parallel BFF work in other worktrees | Hold sibling worktrees (R4, Action Engine R1 already holding); schedule early in Wave 2 |
| 042-044 (legacy code + schema deletion) | Hidden caller of deleted code in non-BFF surface | Wave 4 must follow Wave 9 (chat-summarize); thorough Wave 4 task 040 audit |
| 052 (owner checkpoint) | Owner availability delays critical path | Surface checkpoint requirement upstream; provide CSV review tool |
| 070-075 (skill rewrites) | Future skill executions break if rewrite has bugs | Wave 7 task 075 runs /jps-validate before closing wave |
| 081 (Power Apps form update) | Maker-portal change requires owner action | Coordinate timing with owner; document rollback |

---

## Task Generation Status

### Fully generated (2026-06-28)

All 82 POMLs are generated and committed. Wave 1 can begin immediately on operator say-so.

- Waves 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 — all POML files present
- Each POML follows the exemplar format (XML decl → metadata → prompt → role → goal → inputs → constraints → knowledge → context → steps → tools → outputs → acceptance-criteria)
- FULL-rigor tasks include Step 0 Rigor Declaration + Step 9.5 quality gates (code-review + adr-check)
- ADRs cited per task: ADR-006 (PCF/Fluent v9 — Wave 8), ADR-010 (DI Minimalism), ADR-013 (BFF AI Architecture), ADR-014 (Caching — Wave 9), ADR-021 (Dark Mode — Wave 8), ADR-029 (BFF Publish Hygiene), ADR-038 (Testing Strategy)
- Sub-Agent Write Boundary tasks marked `parallel-safe: false`: 022, 042, 043, 044, 064, 068, 070-075, 100, 101, 090-project-wrap-up

### Subsequent task generation

If new tasks are discovered during execution (DEF-NNN deferrals or ISS-NNN follow-on issues), invoke `/project-defer-issue-tracking` rather than `/task-create` — these are tracked separately per CLAUDE.md §11 + project tracking conventions.

---

*Maintained by `task-execute` (status updates) and `/task-create` (new POML generation). Last updated 2026-06-28 by `/project-pipeline` Step 3.*
