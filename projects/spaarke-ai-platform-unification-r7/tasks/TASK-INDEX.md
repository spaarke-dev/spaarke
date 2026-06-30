# Task Index — Spaarke AI Platform Unification R7

> **Last Updated**: 2026-06-30 (Wave 12 added — MVP Completion: Daily Briefing 6-entity + Wizards + Assistant↔Workspace)
> **Source plan**: [`../plan.md`](../plan.md)
> **Source spec**: [`../spec.md`](../spec.md)
> **Wave 12 plan**: [`../notes/wave12-mvp-completion-plan.md`](../notes/wave12-mvp-completion-plan.md)
> **Total tasks generated**: **96 POML files across 12 waves** (Wave 12 added 2026-06-30 from operator-driven strategic discussion: bring three AI feature groups to functioning-in-production state on 3-6 week horizon)
> **Generation method**: 10 parallel subagents per wave 1-10 (dispatched by `/project-pipeline` Step 3); Wave 11 generated 2026-06-29 via `/task-create --wave 11`; Wave 12 generated manually 2026-06-30 alongside Wave 12 plan doc

## Status Legend

- 🔲 not-started
- 🔄 in-progress
- ✅ completed
- ⏸️ blocked / waiting on dependency
- ❌ abandoned (re-spec required)

## Status Snapshot

| Wave | Goal | Status | Tasks |
|---|---|---|---|
| Wave 1 | AiCompletionNodeExecutor build (FR-12 to FR-15) | 🟢 COMPLETE | 001-010 ✅ all done; publish-hygiene gate PASSED 2026-06-28 (46.71 MB compressed; +1.06 MB vs 45.65 baseline; 0 new HIGH CVE; 20/20 AiCompletionNodeExecutor tests pass) |
| Wave 2 | Dispatch refactor + enum rename (FR-07 to FR-10) | 🟢 COMPLETE (10/10 tasks; publish-hygiene gate PASSED 2026-06-28: 46.71 MB compressed = FLAT vs Wave 1; +1.06 MB cumulative R7 = unchanged vs Wave 1 baseline; 0 new HIGH CVE; AiCompletion 20/20 + Orchestration 60/63 baseline preserved) | 020 ✅, 021 ✅, 022 ✅, 023 ✅, 024 ✅, 025 ✅, 026 ✅, 027 ✅, 028 ✅, 029 ✅ |
| Wave 3 | Typed config schemas (FR-16) | ✅ COMPLETE (7/7 tasks; 46.71 MB FLAT vs Wave 2; 0 new HIGH CVE; 34/34 Wave 3-targeted tests pass) | 030 ✅, 031 ✅, 032 ✅, 033 ✅, 034 ✅, 035 ✅, 036 ✅ |
| Wave 4 | Schema cleanup + remove legacy direct-path (FR-03, FR-04, FR-11) | 🟢 COMPLETE (8/8 tasks; publish-hygiene gate PASSED 2026-06-29: 46.72 MB compressed = +0.005 MB FLAT vs Wave 3 baseline; +1.07 MB cumulative R7 vs pre-R7 baseline; 0 new HIGH CVE; signal GREEN with informational note that expected shrink did not materialize at compressed-size level — see notes/handoffs/wave-4-publish-report.md Interpretation section) | 040 ✅, 041 ✅, 042 ✅, 043 ✅, 044 ✅, 045 ✅, 046 ✅, 047 ✅ |
| Wave 5 | Existing-playbook backfill (FR-19, FR-20) | 🔄 in-progress (050 ✅ Review-PlaybookNodes-Dispatch.ps1 authored + dry-run verified against spaarkedev1: 94 nodes / 41 HIGH / 14 MEDIUM / 23 LOW / 16 NONE; 053 ✅ Migrate-PlaybookNodes-to-ExecutorType.ps1 authored 2026-06-29: 428 LOC, idempotent + dry-run + auto-detect + range-checked + defensive 404; self-test against input CSV produced "no decisions to apply" graceful exit 0 BEFORE auth; live-run gated on 052 owner CSV; 055 ✅ Deploy-Playbook.ps1 R7-modernized 2026-06-29: `$NodeTypeMap` + `sprk_nodetype` write deleted, Lint A executor-type allow-list added (33 known values), backward-compat input map for 17 legacy `nodeType` labels, dry-run output shows `sprk_executortype = N (Name)` per node, both happy-path + lint-failure self-tests verified via pwsh; 052/054 blocked on owner-checkpoint; 056 unblocked when 054 owner CSV migration completes) | 050 ✅, 053 ✅, 055 ✅; 051-052, 054, 056 generated |
| Wave 6 | Documentation deletion + updates (FR-28 to FR-31) | 🔄 in-progress (060 ✅ audit/disposition; 061 ✅ DELETE §5 from playbook-runtime.md; 062 ✅ UPDATE actions-nodes-scopes decision tree; 065 ✅ MAJOR UPDATE JPS-AUTHORING-GUIDE; 066 ✅ MAJOR UPDATE PLAYBOOK-AUTHOR-GUIDE node-first dispatch; 067 ✅ CREATE ai-guide-consumer-wiring.md FR-31; 069 ✅ post-audit NFR-08 PASS — zero new SUPERSEDED markers across 5 Wave-6-touched files (notes/spikes/wave6-nfr08-post-audit.md); 063 blocked on W5, 064 + 068 main-session-only pending) | 060-069 ✅ generated (10 files); 060 ✅, 061 ✅, 062 ✅, 065 ✅, 066 ✅, 067 ✅, 069 ✅ executed |
| Wave 7 | Skill rewrites (FR-32, FR-33) | ⏸️ blocked on Wave 2 | 070-075 ✅ generated (6 files) |
| Wave 8 | Playbook Builder UI updates (FR-21 to FR-27) | 🔄 in-progress (080 ✅ audit; 082 ✅ 33-executor categorized Node Types left panel FR-22; 083 ✅ typed config form renderer infrastructure; 084 ✅ 5 priority typed forms verified + 20 Jest tests (W3-032 already shipped rich BFF schemas; canvas-side test gate added); 085 ✅ remaining 18 placeholder executors enriched with typed fields FR-23 (2026-06-29); 086 ✅ Action tab promotion FR-24; 088 ✅ canvas state `sprk_nodetype`→`sprk_executortype` FR-26 (9 refs replaced, 3 `__actionType` removed, 4 legacy constructs deleted, grep zero verified, build clean); 089a ✅ Jest UI tests for ExecutorTypeSelector dropdown + tier grouping (14/14 pass, 2026-06-29); 089b ✅ +26 incremental jest tests for 5 priority typed forms covering field-count sentinels / default-value resolution / Boolean+Number widget commits / controlled-component re-render / per-executor isolation (FR-23; 46/46 pass combined with task 084); 089c ✅ ADR-021 dark-mode static jest scan (5 Wave 8 files, 1949 LOC, 0 hardcoded color findings; 13 tests pass incl. 6 scanner self-tests; 2026-06-29); 081, 087, 089, 089d pending) | 080-089d ✅ generated (14 files); 080, 082, 083, 084, 085, 086, 088, 089a, 089b, 089c ✅ executed |
| Wave 9 | Consumer migration (FR-17, FR-18) | 🟢 COMPLETE (090 ✅ audit/design; 091 ✅ FR-17 SessionSummarizeOrchestrator migrated; 092 ✅ chat-summarize sprk_playbookconsumer row verified; 093 ✅; 094 ✅ /playbooks hard slash + Library modal browse-mode + PlaybookCardGrid consumer chips; 095 ✅ Library modal wired into Daily Briefing widget via DigestHeader overflow; 096 ✅ Library modal wired into LegalWorkspace Get Started 9th "Browse Playbooks" card — FR-18 ≥3 surfaces acceptance MET; see notes/handoffs/fr18-closure.md) | 090 ✅, 091 ✅, 092 ✅, 093 ✅, 094 ✅, 095 ✅, 096 ✅ |
| Wave 10 | Wrap-up + R4 graduation gate close | 🔄 in-progress (100 ✅ end-to-end verification report 2026-06-29 marked 11/15 PASS at verification-report level, but Wave 10 task 101 UAT discovered orchestrator template-engine gap — Wave 11 added 2026-06-29 to close that gap; 101 + 090-wrap-up NOW BLOCK on Wave 11 task 119 GREEN) | 100 ✅, 101 ⏸️ (blocks on W11-117), 090-project-wrap-up ⏸️ (blocks on W11-119) |
| Wave 11 | Playbook Orchestrator Runtime Variable Resolution + R7 UAT Drive | 🔄 in-progress (110 ✅ + 111 ✅ + 111a ✅ + 112 ✅ + 113 ✅ + 114 ✅ + 115 ✅ 2026-06-29 — Option B Layer 1+2 + 7 helpers + lambda+pipe eliminated + fan-out iteration + ValidateEntityNames smoke-test value REPLACED by source-correct templates; 131/131 Wave 11 + regression tests pass; 0 regressions across 7,532 BFF tests; AiAnalysis wiring deferred via DEF-001; T118 ACHIEVED via POC pivot — DailyBriefingCollector live-render path operator-verified in spaarkedev1; T119 publish gate pending) | 110-119 + 111a ✅ generated; 8/11 executed (T117 substantively SATISFIED by T118 POC) |
| Wave 12 | MVP Completion — Daily Briefing 6-entity + Wizards + Assistant↔Workspace | 🔄 in-progress (W12.1 audits ✅ ALL 4 COMPLETE 2026-06-30; W12.2/12.3/12.4 POMLs ✅ ALL 18 GENERATED 2026-06-30; ready to execute starting at T130) | 120-123 + 130-136 + 140-145 + 150-154 ✅ generated (22 POMLs); 4/22 executed (audits 120-123) |

**Total: 114 POML files generated across 12 waves. Wave 12.2/12.3/12.4 ready for execution. Audits returned: all RESTORATION dispositions (no remediation needed); membership resolver path chosen (T130 fixes IMembershipResolverService root cause).**

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
| 008 | ✅ | xUnit tests — temperature override + per-node prompt override | bff-api, testing | yes (with 007) | 007 |
| 009 | ✅ | xUnit tests — error paths (missing prompt, malformed JSON, LLM error) | bff-api, testing | yes (with 007) | 007 |
| 010 | ✅ | Wave 1 BFF publish + size check (NFR-01) + CVE scan (NFR-02) | bff-api, deploy | yes | 006, 009 |

## Wave 2 — Dispatch refactor + enum rename (FR-07 to FR-10)

**Goal**: collapse 3-layer dispatch to single-hop `node.sprk_executortype` read; rename `ActionType` → `ExecutorType` across BFF.
**Estimated**: ~18 tasks, 3-4 days.
**Critical**: task 022 (enum rename) is a single sequential task — large diff intentionally batched.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 020 | ✅ | Audit all `ActionType` references via grep (464 actual, not ~1000+) | audit, bff-api | yes | W1 complete |
| 021 | ✅ | Plan rename strategy (PR sizing, conflict-risk windows) | planning | yes | 020 |
| 022 | ✅ | Rename C# enum ActionType → ExecutorType (full refactor, BFF only) | bff-api, code-impl, refactor | no (single large diff) | 021 |
| 023 | ✅ | Rename `INodeExecutor.SupportedActionTypes` → `SupportedExecutorTypes` | bff-api, code-impl | yes | 022 |
| 024 | ✅ | Update `PlaybookOrchestrationService.ExecuteNodeAsync` to read single-hop (FR-07) | bff-api, code-impl | yes | 022 |
| 025 | ✅ | Delete structural fallback ladder (FR-08, ~150 LOC) | bff-api, code-impl | yes | 024 |
| 026 | ✅ | Delete Action ActionType override branch lines 1241-1278 (FR-09) | bff-api, code-impl | yes (with 025) | 024 |
| 027 | ✅ | Update `NodeExecutorRegistry` dispatch to use `ExecutorType` | bff-api, code-impl | yes (with 025-026) | 023 |
| 028 | ✅ | Update `AnalysisActionService` Action read path | bff-api, code-impl | yes (with 025-027) | 024 |
| 029 | ✅ | Wave 2 BFF publish + size check (NFR-01) + CVE scan | bff-api, deploy | yes | 022-028 |

## Wave 3 — Typed config schemas (FR-16)

**Goal**: each `INodeExecutor` declares config schema; BFF serves schemas to PlaybookBuilder.
**Estimated**: ~8 tasks, 2 days. Can start after Wave 1.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 030 | ✅ | Design GetConfigSchema() signature + schema DTO shape | bff-api, planning | yes | W1 complete |
| 031 | ✅ | Add GetConfigSchema() to INodeExecutor interface | bff-api, code-impl | yes | 030 |
| 032 | ✅ | Implement GetConfigSchema() on 25 concrete executors (5 rich + 20 placeholder) | bff-api, code-impl | yes | 031 |
| 033 | ✅ | Implement BFF endpoint GET /api/ai/playbook-builder/executor-config-schemas | bff-api, code-impl | yes | 032 |
| 034 | ✅ | xUnit tests for endpoint + schema serialization | bff-api, testing | yes | 033 |
| 035 | ✅ | Document schema shape in `docs/architecture/AI-ARCHITECTURE.md` | docs | yes | 030 |
| 036 | ✅ | Wave 3 BFF publish + size check (NFR-01) | bff-api, deploy | yes | 033, 034 |

## Wave 4 — Schema cleanup + remove legacy direct-path (FR-03, FR-04, FR-11)

**Goal**: delete unused columns + legacy `ExecuteAnalysisAsync`.
**Estimated**: ~8 tasks, 1-2 days. **Must follow Wave 9** (chat-summarize migration).

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 040 | ✅ | Audit all callers of AnalysisOrchestrationService.ExecuteAnalysisAsync | audit, bff-api | yes | — |
| 041 | ✅ | Migrate non-chat callers to PlaybookOrchestrationService.ExecuteAsync (FR-11) — Wave 9 dep RESCINDED per task 040 audit | bff-api, code-impl | yes | 040 only (audit confirmed Wave 9 was false-premise dep) |
| 042 | ✅ | DELETE ExecuteAnalysisAsync + cascading dead code (FR-11) | bff-api, code-impl, deletion | no | 041 |
| 043 | ✅ | Drop sprk_analysisaction.sprk_actiontypeid (lookup) — Web API DELETE on ManyToOneRelationship (Lookups don't accept DELETE on /Attributes; cascade-via-relationship is the supported form); pre-cleared deps from "Active Analysis Actions" savedquery + "Analysis Action main form" SystemForm; 2026-06-29 | dataverse-schema, deletion | no | 042 |
| 044 | ✅ | Drop sprk_analysisaction.sprk_executoractiontype (INT) — straight Web API DELETE on /Attributes (no relationship cascade needed for primitives); same form pre-clean as 043; 2026-06-29 | dataverse-schema, deletion | no (sequential with 043) | 043 |
| 045 | ✅ | Document sprk_analysisactiontype as decorative (FR-05) | docs | yes | — |
| 046 | ✅ | Update AnalysisActionService to remove references to dropped fields | bff-api, code-impl | yes (after 044) | 044 |
| 047 | ✅ | Wave 4 BFF publish + size check (expect SHRINK) — actual: 46.72 MB compressed, +0.005 MB FLAT vs Wave 3 (expected shrink did not materialize at compressed-size level; +1.07 MB cumulative R7 unchanged vs Wave 3; 0 new HIGH CVE; signal GREEN); 2026-06-29 | bff-api, deploy | yes | 042-046 |

## Wave 5 — Existing-playbook backfill (FR-19, FR-20)

**Goal**: populate `sprk_executortype` on 94 existing nodes in spaarkedev1; update Deploy-Playbook.ps1.
**Estimated**: ~7 tasks, 2-3 days (includes owner-review checkpoint).
**Sequential within wave** (owner checkpoint at task 052).

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 050 | ✅ | Author Review-PlaybookNodes-Dispatch.ps1 (FR-19) | script, dataverse | no (sequential) | 024 (W2 dispatch ready) |
| 051 | ✅ | Run review tool; produce CSV for owner review | dataverse, audit | no | 050 |
| 052 | ✅ | Owner review of 94-node review CSV — assisted via Prefill-PlaybookNodeReview.ps1 (78 AUTO-COPY + 16 AUTO-GUESS pre-fills, owner approved "looks generally ok" path); CSV committed at `ce0e9bfce`. 2026-06-29. | manual, owner-gate | no | 051 |
| 053 | ✅ | Author Migrate-PlaybookNodes-to-ExecutorType.ps1 (idempotent + dry-run) — 428 LOC, auto-detect decision column (5 candidates), range-checked against KnownExecutorTypeValues, defensive 404 handling, pre-scan validates BEFORE auth, self-test "no decisions to apply" graceful exit 0; live-run gated on 052 owner CSV; 2026-06-29 | script, dataverse | no | 052 |
| 054 | ✅ | Migration LIVE-RUN against spaarkedev1 — 94/94 PATCHed (38.8s), 0 errors, 0 404s. Idempotency confirmed (2nd dry-run: 94 already-correct, 0 WOULD PATCH). MCP spot-check of DAILY-BRIEFING-NARRATE (7b5a6ed3-0271-f111-ab0e-000d3a13a4cd) verified all 6 nodes: Start→33, LoadKnowledge→142, GenerateTldr→1, GenerateChannelNarratives→1, ValidateEntityNames→141, ReturnResponse→143. R4 graduation gate target playbook ready for /narrate UAT (task 101). 2026-06-29. | dataverse, deploy | no | 053 |
| 055 | ✅ | Update Deploy-Playbook.ps1 to write executor type explicitly (FR-20) — `$NodeTypeMap` + `sprk_nodetype` write removed, Lint A added (33-value allow-list), backward-compat input map for 17 legacy `nodeType` friendly labels, dry-run output shows `sprk_executortype = N (Name)` per node, happy-path (4 nodes incl. legacy `nodeType: AIAnalysis` + explicit `executorType: 33/1/42`) + lint-failure (executorType=9999 + unknown legacy label) both verified via pwsh dry-run 2026-06-29; live sanity redeploy is task 056 | script | no | 054 (live), but script itself ready now |
| 056 | ⏸️ | Sanity — redeploy 3 representative playbooks (Daily Briefing, Insights, chat) | dataverse, deploy | no | 055 |

## Wave 6 — Documentation deletion + updates (FR-28 to FR-31)

**Goal**: DELETE outdated R4 canonical-truth sections, UPDATE current sections, CREATE consumer-wiring guide.
**Estimated**: ~12 tasks, 2-3 days. Can parallelize with Wave 5-8.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 060 | ✅ | Audit ai-architecture-playbook-runtime.md for outdated sections | docs, audit | yes | 024 |
| 061 | ✅ | DELETE §5 action-lookup precedence ladder + structural-fallback section | docs, deletion | yes (with 062-066) | 060 |
| 062 | ✅ | UPDATE ai-architecture-actions-nodes-scopes.md 4-Home decision tree | docs | yes | 024 |
| 063 | ✅ | UPDATE ai-guide-playbook-deploy-recipe.md — header bump + JSON example (explicit `executorType` + backward-compat `nodeType`) + R7 §3 step-1 Lint A narrative + §9 lint-failure troubleshooting subsection (FR-28 + FR-20 doc impact). Main session per Sub-Agent Write Boundary. 2026-06-29. Commit `2e96c0f70`. | docs | yes | 055 |
| 064 | ✅ | UPDATE `.claude/constraints/bff-extensions.md` §G (FR-29) — rewritten for R7 single-hop dispatch contract; Hot-Path Declaration renumbered §G → §H to fix duplicate-§G ambiguity. 2026-06-29. Commit `5a915292c`. | docs, skill-directives | no (sequential per Sub-Agent Write Boundary) | 024 |
| 065 | ✅ | MAJOR UPDATE JPS-AUTHORING-GUIDE.md (FR-30) | docs | yes | 024 |
| 066 | ✅ | MAJOR UPDATE PLAYBOOK-AUTHOR-GUIDE.md (FR-30) | docs | yes | 024 |
| 067 | ✅ | CREATE ai-guide-consumer-wiring.md (FR-31) | docs | yes | 091 (W9 chat-summarize done) |
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
| 081 | ✅ | Replace Node Type field with Executor Type Choice on Power Apps form (FR-21) — empirical pre-state: form was already wired (sprk_executortype control present, sprk_nodetype absent); only DefaultFormValue (-1 → 1 AiCompletion) needed updating. PUT on PicklistAttributeMetadata typed-cast + PublishXml; 2026-06-29. UAT spot-check ⏸️ operator-only. See [`notes/spikes/081-form-update-actuals.md`](../notes/spikes/081-form-update-actuals.md) | dataverse, form-update | yes | 024 |
| 082 | ✅ | Update canvas Node Types left panel — 33 categorized entries (FR-22) | code-page, ui | yes | 024 |
| 083 | ✅ | Wire typed config form renderer driven by schema endpoint (FR-23) | code-page, ui | yes | 033 (W3 endpoint) |
| 084 | ✅ | Implement typed config forms for 5 priority executors (FR-23) | code-page, ui | yes | 083 |
| 085 | ✅ | Implement remaining 18 placeholder executor schemas (FR-23; 28 in POML but only 18 have INodeExecutor impl — 10 enum values without files don't reach the registry endpoint) | code-page, ui, bff-api | yes | 084 |
| 086 | ✅ | Promote Action selection to new Action tab (FR-24) | code-page, ui | yes | 082 |
| 087 | ⏸️ | KEEP Prompt tab + per-node override wiring (FR-25) — UAT | code-page, ui, testing | yes | 086 |
| 088 | ✅ | Replace `sprk_nodetype` references in canvas state (FR-26) | code-page, code-impl | yes | 080 |
| 089 | ⏸️ | Handle unknown-executor-type warning state (FR-27) | code-page, ui | yes | 085 |
| 089a | ✅ | UI test — Executor Type dropdown + tier grouping render (Jest, 14/14 pass; rescoped from browser to component test) | code-page, testing, ui-test | yes | 089 |
| 089b | ✅ | UI test — typed config forms for 5 priority executors (+26 incremental jest tests; 46/46 pass when combined with task 084's 20) | code-page, testing, ui-test | yes | 084 |
| 089c | ✅ | UI test — ADR-021 dark mode compliance (static jest scan; 13 tests pass; 0 hardcoded color findings across 5 Wave 8 files, 1949 LOC; rescoped from browser to grep-based commit-time gate) | code-page, testing, ui-test | yes | 081-088 |
| 089d | ⏸️ | Deploy PlaybookBuilder Code Page to spaarkedev1 | code-page, deploy | yes | 089a-c |

## Wave 9 — Consumer migration (FR-17, FR-18)

**Goal**: migrate chat-summarize + wire Playbook Library into ≥3 consumer surfaces.
**Estimated**: ~7 tasks, 2 days.
**Order matters**: tasks 090-091 (chat-summarize) must precede Wave 4 task 042 (ExecuteAnalysisAsync deletion).

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 090 | ✅ | Audit SessionSummarizeOrchestrator caller graph; design Path A.5 migration | audit, bff-api | yes | 024 |
| 091 | ✅ | Migrate SessionSummarizeOrchestrator to IConsumerRoutingService + IInvokePlaybookAi (FR-17) | bff-api, code-impl | yes | 090 |
| 092 | ✅ | Add chat-summarize row to sprk_playbookconsumer table | dataverse | yes | 091 |
| 093 | ✅ | Audit Playbook Library Code Page modal current routing | audit, code-page | yes | — |
| 094 | ✅ | Wire Library modal into spaarke-ai chat surface (FR-18) | code-page, ui | yes | 093 |
| 095 | ✅ | Wire Library modal into briefing widget (FR-18) | code-page, ui | yes | 094 |
| 096 | ✅ | Wire Library modal into ad-hoc launcher (FR-18) — LegalWorkspace 9th Get Started card | code-page, ui | yes | 095 |

## Wave 10 — Wrap-up

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 100 | ✅ | End-to-end verification of 15 success criteria — 11/15 PASS at verification-report level, but Wave 11 added 2026-06-29 to close UAT-discovered orchestrator gap | testing, audit | no | All waves done |
| 101 | ⏸️ | UAT — /narrate via Daily Briefing widget (R4 graduation gate, FR-15) — BLOCKED on W11-117 | uat, testing | no | 100, **W11-117** |
| 090-project-wrap-up | ⏸️ | Wrap-up — README → Complete, lessons-learned.md, archive — BLOCKED on W11-119 | wrapup | no | 101, **W11-119** |

## Wave 11 — Playbook Orchestrator Runtime Variable Resolution + R7 UAT Drive

> Added 2026-06-29 after Wave 10 task 100 marked the verification report GREEN but Wave 10 task 101 (UAT) discovered the actual root cause of empty `/narrate` end-to-end: `PlaybookOrchestrationService` only does literal `{{paramName}}` substitution; the deployed DAILY-BRIEFING-NARRATE playbook uses richer expressions (`{{json start}}`, `{{nodeName.field}}`, `{{map}}`, `{{flatten}}`, `{{distinct}}`, `{{concat}}`, `{{join}}`, `{{flatMap}}`, fan-out iteration). Wave 11 closes that gap by wiring the existing `ITemplateEngine` (Handlebars.NET) into the orchestrator + carrying node outputs forward as context + registering 7 helpers + implementing fan-out iteration semantics + restoring source-correct ValidateEntityNames config + UAT.

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 110 | ✅ | Audit current orchestrator template resolution + design RunContext.NodeOutputs surface — design doc 2026-06-29 at `notes/spikes/wave11-orchestrator-resolution-design.md`; key finding: NodeOutputs infra ALREADY exists; T111 work narrower than initially scoped (extract `BuildTemplateContext` from ReturnResponseNodeExecutor as shared helper) | audit, bff-api, planning | yes | — |
| 111 | ✅ | Wire ITemplateEngine into orchestrator (Layer 1) + extend PromptSchemaRenderer with structured `## Input` section (Layer 2) — **Option B shipped 2026-06-29**: PlaybookTemplateContextBuilder + orchestrator.ApplyConfigJsonTemplates rewrite + PromptSchemaRenderer.runtimeInput param + AiCompletion.ExtractInputBindingAsJsonElement + Load/Return BuildTemplateContext refactor; 13/13 new tests pass, 0 regressions across 7,514 other passing tests; AiAnalysis wiring deferred via DEF-001 (~6 files; not needed for R7 UAT target) | bff-api, code-impl, ai, refactoring | yes | 110 |
| 111a | ✅ | Document the Playbook-driven LLM Output pattern (architecture doc + maker guide + cross-links) — 2026-06-29: `SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md` (385 lines, 12 sections incl. component model + 6-step runtime trace + failure modes + Insight Engine worked example) + `BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md` (440 lines, decision tree + 6-step tutorial + 2 worked examples) + root CLAUDE.md §17 pointer rows + cross-links from AI-ARCHITECTURE.md / ai-architecture-actions-nodes-scopes.md / ai-architecture-playbook-runtime.md / JPS-AUTHORING-GUIDE.md | documentation, architecture, ai | no (sequential after T111 ships) | 111 |
| 112 | ✅ | Register custom Handlebars helpers (2026-06-29): json + map + flatten + distinct + concat + join — 12 new tests + subexpression-composition test pass | bff-api, code-impl, ai | yes | 111 |
| 113 | ✅ | Eliminate `{{lambda}}` + pipe shorthand from source playbook (2026-06-29): added flatMap helper + 3 new tests + rewrote allowList (lambda → flatMap) and candidateText (pipe → nested subexpr) in daily-briefing-narrate.json — grep confirms zero remaining lambda/pipe in runtime expressions | bff-api, code-impl, ai, dataverse-data | yes | 111 |
| 114 | ✅ | Fan-out iteration semantics in orchestrator (2026-06-29): TryExtractIterationConfig + ExecuteFanOutIterationAsync + StripIterationBlock + TryParseIterationItems; per-iteration overlay context binding via itemAlias; aggregate composite NodeOutput; 5 detection-helper unit tests + T116 integration coverage | bff-api, code-impl, ai | yes | 111 |
| 115 | ✅ | Restore source-correct ValidateEntityNames node configJson + author Sync-DailyBriefingNarratePlaybookNodes.ps1 (2026-06-29) — script mirrors Sync-BriefNarrateOutputSchemas.ps1 pattern; 6/6 deployed sprk_playbooknode rows PATCHed from canonical source; MCP read_query confirms ValidateEntityNames smoke-test value `"smoke test text"` GONE, replaced by source-correct flatMap/nested-subexpr templates that resolve cleanly via T112+T113+T114 Layer 1 + helpers + iteration | dataverse, deploy, script | no (touches deployed data) | 112, 113, 114 |
| 116 | 🔲 | Build BFF + deploy via bff-deploy; smoke /narrate via curl with realistic payload | deploy, smoke, bff-api | no (deploys + smokes) | 115 |
| 117 | 🔲 | UAT — Daily Briefing widget renders TL;DR + per-channel narratives with real data (R4 graduation) | uat, gate, operator | no (operator UAT) | 116 |
| 118 | 🔲 | Address operator-flagged UAT issues (events, links/tools, two unidentified items) | investigation, uat | no (operator-context-dependent) | 117 |
| 119 | 🔲 | Wave 11 BFF publish + size check (NFR-01) + CVE scan (NFR-02) | bff-api, deploy | yes | 118 |

## Wave 12 — MVP Completion (audits-first; broader scope than original R7)

> Added 2026-06-30 from operator-driven strategic discussion. Three deliverable groups: Daily Briefing 6-entity coverage + UI fixes; restoration/remediation of 5 broken wizards (file summary, doc create profile, three Prefill wizards Create Matter/Project/Work Assignment); Assistant↔Workspace audit + fixes per audit findings. Full scope + plan at [`../notes/wave12-mvp-completion-plan.md`](../notes/wave12-mvp-completion-plan.md).
>
> **Why Wave 12 exists**: R7's W11 POC pivot validated the code-defined narrator pattern but only for ONE consumer (Daily Briefing). Wave 12 absorbs the coordinated MVP push to bring 3 feature groups to functioning state. Operator-stated: "we have to focus on 1 and 2 today and after those are working then we can focus on the rest."
>
> **Sub-wave structure**: W12.1 audits (read-only, parallel-safe) → W12.2 Daily Briefing → W12.3 Wizards → W12.4 Assistant↔Workspace → W12.5 wrap-up. W12.2-12.5 POMLs generated after W12.1 audits return findings.

### Wave 12.1 — Audits (READY TO EXECUTE; all 4 parallel-safe, read-only)

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 120 | ✅ | Audit Assistant↔Workspace — PLUMBING-ONLY (2-4d); naming-split + EntityName + PageType gaps | audit, bff-api, code-page, assistant, wave12 | yes | — |
| 121 | ✅ | Audit wizard file summary — RESTORE (1-3hr config drift; SIMPLE playbook healthy) | audit, bff-api, wizard, playbook, wave12 | yes | — |
| 122 | ✅ | Audit document create profile — Dataverse data fix + UI cosmetic (2-4hr) | audit, bff-api, wizard, playbook, document, wave12 | yes | — |
| 123 | ✅ | Audit three Prefill wizards — RESTORE all 3 (Project FK 1-2hr; Matter smoke; WA inherits) — architecture already shared, NO new abstractions needed | audit, bff-api, code-page, wizard, playbook, prefill, wave12 | yes | — |
| 124 | 🔲 | AUDIT — Wave 5 backfill-health sweep (T142+T143 surfaced systemic issue; sweep all backfilled nodes for stub configJson + orphan FK + clobbering overrides) | audit, dataverse, backfill, wave5, follow-up, wave12 | yes | — |

### Wave 12.2 — Daily Briefing (READY TO EXECUTE — 7 POMLs generated 2026-06-30)

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 130 | ✅ | Diagnose + fix IMembershipResolverService root cause (operator-chosen durable path) — root cause: empty default `IncludedIdentityTables`; fix via `MembershipOptionsDefaults` post-configure; 3 regression tests added; commit `451603bac` | bff-api, code-impl, dataverse, membership, wave12 | yes | — |
| 131 | 🔲 | Extend DailyBriefingCollector to 6 entity types + use IMembershipResolverService | bff-api, code-impl, daily-briefing, wave12 | no | 130 |
| 132 | 🔲 | TLDR↔Activity Notes consistency — chain TLDR as input to channel narratives | bff-api, code-impl, ai, narrator, wave12 | yes | — |
| 133 | ✅ | Channel registry expansion (BFF response shape + widget CHANNEL_REGISTRY) | bff-api, pcf, widget, wave12 | yes | — |
| 134 | ✅ | Widget tools — 'Add To Do' checkmark + preserve three-dot menu | pcf, react, fluent-ui, widget, wave12 | yes | ee12e172e |
| 135 | 🔲 | Verify per-bullet entity links work for all 6 entity types | bff-api, narrator, testing, wave12 | no | 131 |
| 136 | 🔲 | Wave 12.2 deploy + smoke + UAT (combinable with T154 deploy event) | deploy, smoke, uat, gate, wave12 | no | 130-135 |

### Wave 12.3 — Wizards (READY TO EXECUTE — 6 POMLs generated 2026-06-30; all RESTORATION)

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 140 | ✅ | RESTORE wizard file summary (App Service `Workspace__SummarizePlaybookId` env-var was MISSING → set to `4a72f99c-a119-f111-8343-7ced8d1dc988`; defense-in-depth fallback restored; consumer routing row already correct; healthz green post-restart; operator UAT in T145; 2026-06-30) | wizard, deploy, config, smoke, wave12 | yes | — |
| 141 | ✅ | RESTORE document create profile (Dataverse data-only fix: DELETE broken Save Profile node `c9334fb7-...` + PATCH Update Record node 3 fieldMappings to align with 7 JPS fields; ACT-011 JPS preserved; UI cosmetic deferred; 2026-06-30) | wizard, document, dataverse, config, wave12 | yes | — |
| 142 | ✅ | RESTORE Create Project — single Dataverse PATCH (sprk_actionid FK re-link per audit 123) | wizard, project, dataverse, data-fix, wave12 | yes | — |
| 143 | ✅ | SMOKE Create Matter wizard — code-path verification (no browser smoke from sandbox); found 2 deterministic data-layer bugs (EntityNameValidator stub configJson + AI Analysis systemPrompt-override clobber); minimal fix recommended (data-only, operator must approve); see audit 123 §10; T144 inherits via Matter endpoint; 2026-06-30 | wizard, matter, smoke, wave12 | yes | — |
| 144 | ✅ | VERIFY Create Work Assignment inherits Matter fix — code-path trace confirms WA AI prefill calls `/api/workspace/matters/pre-fill` (literal at `EnterInfoStep.tsx:175`); T143 fix (commit `3cb239e5d`) applies to WA by construction; record-copy prefill path is client-side OData unaffected; NO CODE CHANGE; end-to-end smoke deferred to T145 UAT; see audit 123 §11; 2026-06-30 | wizard, work-assignment, smoke, wave12 | no | 143 |
| 145 | 🔲 | UAT — all 5 wizards end-to-end (Wave 12.3 gate) | wizard, uat, gate, wave12 | no | 140-144 |

### Wave 12.4 — Assistant↔Workspace (READY TO EXECUTE — 5 POMLs generated 2026-06-30; PLUMBING-ONLY)

| ID | Status | Title | Tags | Parallel-safe | Dependencies |
|---|---|---|---|---|---|
| 150 | ✅ | Gap A — Naming normalization at BFF boundary (most-impactful single fix) — commit `287e7b0a9` | bff-api, code-impl, assistant, normalization, wave12 | yes | — |
| 151 | 🔲 | Gap B — Server-side EntityName lazy-fetch in PlaybookChatContextProvider | bff-api, code-impl, assistant, wave12 | yes | — |
| 152 | 🔲 | Gap C — Default PageType in PlaybookChatContextProvider | bff-api, code-impl, assistant, wave12 | yes | — |
| 153 | 🔲 | Audit 120 Gaps D-H — apply remaining 5 smaller fixes per §5 disposition table | bff-api, code-impl, assistant, wave12 | yes | — |
| 154 | 🔲 | Assistant↔Workspace UAT (Wave 12.4 gate) | uat, gate, assistant, wave12 | no | 150-153, 136 |

### Wave 12.5 — Wrap-up (PLACEHOLDER)

| Anticipated ID | Title | Notes |
|---|---|---|
| 160 | Wave 12 UAT close-out across all 3 deliverable groups | |
| 161 | Wave 12 lessons-learned doc | Informs follow-on projects (tunable config, descriptors, retrieval) |
| 162 | Wave 12 final BFF publish + size + CVE gate | |

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
| W10 | 100 ✅; 101 + wrap-up | W11 119 ✅ | Sequential — 101 + wrap-up block on Wave 11 close |
| W11-A | 110 | W5 + W8 ✅ | yes — audit only |
| W11-B | 111 | 110 ✅ | yes — orchestrator wiring (Option B) |
| W11-B' | 111a | 111 ✅ | no — sequential after T111 ships so docs reflect shipped code |
| W11-C | 112, 113, 114 | 111a ✅ (binding doc dep) | yes — 3 in parallel (different code surfaces) |
| W11-D | 115 | 112+113+114 ✅ | no — touches deployed Dataverse data (sequential) |
| W11-E | 116 | 115 ✅ | no — deploys BFF + smoke (sequential) |
| W11-F | 117 | 116 ✅ | no — operator UAT (sequential) |
| W11-G | 118 | 117 ✅ | no — operator-context-dependent (may spawn sub-tasks 118a/b/c) |
| W11-H | 119 | 118 ✅ | yes — hygiene gate |
| W12-1-Audit | 120, 121, 122, 123 | — | yes — all 4 read-only audits parallel-safe; dispatch in one message with 4 task-execute invocations |
| W12-2-DB | 130-136 (placeholder) | 120 ✅ | post-audit POML generation; mostly sequential within (collector → membership → UI fixes → deploy) |
| W12-3-Wiz | 140-145 (placeholder) | 121, 122, 123 ✅ | post-audit POML generation; mostly parallel-safe if independent file surfaces |
| W12-4-Asst | 150-152 (placeholder) | 120 ✅ | post-audit POML generation; sequential (plumbing → matter awareness → UAT) |
| W12-5-Wrap | 160-162 (placeholder) | W12.2-12.4 ✅ | sequential wrap-up |

### Parallel-safety summary

- **Sub-Agent Write Boundary tasks** (must run sequentially, main session only): 022, 042, 043, 044, 064, 068, 070, 071, 072, 073, 074, 075, 100, 101, 090-project-wrap-up
- **Wave 11 sequentiality** (operator + data + UAT gates, not Sub-Agent Boundary): 115, 116, 117, 118
- **All other tasks**: parallel-safe within their group

---

## Critical Path

The longest dependency chain through the WBS (estimated 15-19 days; +3-4 days for Wave 11 added 2026-06-29):

```
001 → 002 → 003 → 006 → 010 (Wave 1 complete)
  → 020 → 021 → 022 (enum rename — single large diff)
  → 024 (dispatch refactor)
  → 050 → 051 → 052 (owner review) → 053 → 054 → 055 → 056 (backfill complete)
  → 089d (PlaybookBuilder UI deployed) — depends on W8 critical path 080 → 082 → 089a-c
  → 100 (verification report)
  → 110 → 111 → {112, 113, 114 in parallel} → 115 → 116 → 117 (R4 graduation UAT) → 118 → 119 (Wave 11)
  → 101 → 090-project-wrap-up
```

Other waves run in parallel where dependencies allow.

**Why Wave 11 was inserted between Wave 10 task 100 and 101**: task 100's verification report was at the "criteria-as-described-in-spec" level (15/15 GREEN). Task 101 (UAT) attempted to exercise /narrate end-to-end and discovered the orchestrator template-engine gap that prevents the actual user experience from working. Wave 11 closes that gap. Without Wave 11, R7 cannot ship — UAT cannot pass. With Wave 11, R7 can close.

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
