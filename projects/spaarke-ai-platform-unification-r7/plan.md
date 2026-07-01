# Implementation Plan — Spaarke AI Platform Unification R7

> **Last Updated**: 2026-06-28
> **Owner**: ralph.schroeder@hotmail.com
> **Source spec**: [spec.md](./spec.md) (438 lines, 33 FRs)
> **Source design**: [design.md](./design.md) v0.6 (552 lines)

## Executive Summary

### Purpose

Collapse Spaarke's three-layer playbook dispatch model into a single typed Choice column (`sprk_executortype`) on `sprk_playbooknode`, build the missing `AiCompletionNodeExecutor` to close R4's `/narrate` end-to-end, promote `sprk_playbookconsumer` to first-class, add typed config schemas per executor, and migrate all 94 existing playbook nodes in spaarkedev1.

### Scope boundaries

In: dispatch reform + AiCompletionNodeExecutor + typed schemas + Playbook Builder UI + Consumer migration + migration scripts + documentation cleanup + skill rewrites + 94-node backfill.

Out: Action Engine R1 territory (Spaarke Claw, Tool Registry classification, gate resolvers, three meta-tools, Action Templates, agent UX), polished maker UX, multi-tenant rollout, backward-compat shims, external consumer-routing doc updates.

### Estimated effort

**Range: 18-25 working days** across 10 waves. Critical path runs through Wave 1 (AiCompletionNodeExecutor) → Wave 2 (dispatch refactor + enum rename) → Wave 5 (backfill) → Wave 8 (Playbook Builder UI) → Wave 10 (wrap-up). Waves 3, 6, 7, 9 can run partially in parallel with the critical path.

## Architecture Context

### Key architectural constraints

- **ADR-010** (DI Minimalism): AiCompletionNodeExecutor registration follows existing module pattern (Singleton, no extra abstraction layers)
- **ADR-013** (BFF AI Architecture): R7 deepens the existing model; `IInvokePlaybookAi` triangle stays canonical
- **ADR-014** (Caching): `IConsumerRoutingService` 5-min TTL preserved
- **ADR-029** (BFF Publish Hygiene): per-task publish-size + CVE verification (binding ceiling 60 MB compressed)
- **ADR-037** (Multinode Output Composition): R7 does NOT affect composite output; `DeliverComposite` continues to work
- **CLAUDE.md §10 BFF Hygiene**: every BFF-touching task runs `.claude/constraints/bff-extensions.md` checklist

### Technology stack

- **Backend**: .NET 8 Minimal API (`Sprk.Bff.Api`), C# 12, xUnit for tests
- **Frontend**: React 18 + Fluent UI v9 (PlaybookBuilder Code Page at `src/client/code-pages/PlaybookBuilder/`)
- **Schema**: Dataverse (`sprk_playbooknode`, `sprk_analysisaction`, `sprk_analysisactiontype`, `sprk_playbookconsumer`)
- **Migration scripts**: PowerShell 7 + Web API + PublishXml pattern (model on `Add-EntityNameValidatorNodeTypeOption.ps1`)
- **AI**: Azure OpenAI via `IOpenAiClient.GetStructuredCompletionRawAsync`

### Integration points

- BFF endpoints: new `GET /api/ai/playbook-builder/executor-config-schemas`, modified `ChatEndpoints` for chat-summarize migration
- Dataverse: `sprk_playbooknode` (Choice column add DONE), `sprk_analysisaction` (drop 2 fields), `sprk_playbookconsumer` (promote to first-class)
- PlaybookBuilder canvas state schema migration (`sprk_nodetype` → `sprk_executortype`)
- jps-* skill bodies → node-first dispatch model

## Discovered Resources

### Applicable ADRs (full content loaded per task)

- [ADR-010 — DI Minimalism](../../docs/adr/ADR-010-di-minimalism.md) — Singleton DI registration pattern for AiCompletionNodeExecutor
- [ADR-013 — BFF AI Architecture](../../docs/adr/ADR-013-bff-ai-architecture.md) — `IInvokePlaybookAi` facade triangle (canonical)
- [ADR-014 — Caching](../../docs/adr/ADR-014-caching.md) — 5-min TTL pattern for `IConsumerRoutingService`
- [ADR-029 — BFF Publish Hygiene](../../docs/adr/ADR-029-bff-publish-hygiene.md) — Per-task publish-size + CVE verification
- [ADR-037 — Multinode Output Composition](../../docs/adr/ADR-037-multinode-output-composition.md) — `DeliverComposite` unaffected by dispatch reform

### Constraints (binding)

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — pre-merge checklist + Null-Object kill-switch pattern + asymmetric-registration rule
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — BFF publish-size verification rule

### Patterns

- [`.claude/patterns/ai/node-executor-authoring.md`](../../.claude/patterns/ai/node-executor-authoring.md) — sibling-pattern reference for AiCompletionNodeExecutor

### Knowledge docs

- [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md)
- [`docs/architecture/ai-architecture-playbook-runtime.md`](../../docs/architecture/ai-architecture-playbook-runtime.md) — §5 + structural-fallback section to DELETE (FR-28)
- [`docs/architecture/ai-architecture-consumer-routing.md`](../../docs/architecture/ai-architecture-consumer-routing.md) — READ-ONLY in R7 (chat-routing-redesign-r1 owns)
- [`docs/architecture/ai-architecture-actions-nodes-scopes.md`](../../docs/architecture/ai-architecture-actions-nodes-scopes.md) — 4-Home decision tree to REWRITE (FR-28)
- [`docs/guides/ai-guide-playbook-deploy-recipe.md`](../../docs/guides/ai-guide-playbook-deploy-recipe.md) — Control-flow name-detection steps to DELETE (FR-28)
- [`docs/guides/JPS-AUTHORING-GUIDE.md`](../../docs/guides/JPS-AUTHORING-GUIDE.md) — MAJOR UPDATE (FR-30)
- [`docs/guides/PLAYBOOK-AUTHOR-GUIDE.md`](../../docs/guides/PLAYBOOK-AUTHOR-GUIDE.md) — MAJOR UPDATE (FR-30)

### Applicable skills

- `dataverse-create-schema` — schema field drops (FR-03, FR-04)
- `dataverse-deploy` — migration script deployment + playbook redeploys
- `bff-deploy` — BFF deploys post Wave 1/2/4
- `code-page-deploy` — PlaybookBuilder deploys (Wave 8)
- `jps-action-create`, `jps-playbook-design`, `jps-playbook-audit`, `jps-validate`, `jps-scope-refresh` — REWRITE targets (Wave 7)
- `code-review`, `adr-check` — Step 9.5 quality gates (FULL rigor)
- `script-aware` — discover existing scripts before writing new ones

### Scripts

- **NEW**: `scripts/dataverse/Review-PlaybookNodes-Dispatch.ps1` (FR-19, Wave 5)
- **NEW**: `scripts/dataverse/Migrate-PlaybookNodes-to-ExecutorType.ps1` (FR-19, Wave 5)
- **UPDATE**: `Deploy-Playbook.ps1` (FR-20, Wave 5) — writes node executor type explicitly; no more name-detection
- **REUSE PATTERN**: `scripts/dataverse/Add-EntityNameValidatorNodeTypeOption.ps1` — R4-era Web API + PublishXml model

### Canonical implementations to follow

- `EntityNameValidatorNodeExecutor` — sibling pattern for AiCompletionNodeExecutor (Singleton, ILogger only, ConfigJson read + Validate). File: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EntityNameValidatorNodeExecutor.cs`
- `PromptSchemaOverrideMerger` — existing; AiCompletionNodeExecutor reuses for per-node Role/Task/Constraints/Output Fields overrides (Q2 KEEP)
- `IOpenAiClient.GetStructuredCompletionRawAsync` — existing structured-completion call AiCompletionNodeExecutor invokes
- `IConsumerRoutingService` — existing; FR-17 chat-summarize migration uses this via Path A.5
- `AnalysisServicesModule.AddNodeExecutors` — DI registration target

## Implementation Approach

### Wave structure (11 waves, ~90-120 tasks)

The work decomposes into 11 waves. Waves 1, 2, 4, 5, 11, 10 are on the critical path. Waves 3, 6, 7, 8, 9 can partially parallelize.

```
Wave 1 (AiCompletion) ─┐
                       ├──► Wave 2 (dispatch + rename) ──► Wave 4 (cleanup) ──► Wave 5 (backfill) ──► Wave 11 (orch runtime resolution + UAT drive) ──► Wave 10 (wrap-up)
Wave 3 (typed schemas)─┘                                          │
                                                                  ├──► Wave 8 (Playbook Builder UI)
Wave 6 (docs cleanup) ─────────────────────────────────┐          │
                                                       └──────────┤
Wave 7 (skill rewrites) ────────────────────────────────────────┐ │
                                                                ├─┘
Wave 9 (consumer migration) ────────────────────────────────────┘
```

### Critical path

Wave 1 → Wave 2 → Wave 5 → Wave 8 (UI completeness) → **Wave 11 (UAT drive)** → Wave 10. Estimated 15-19 working days (Wave 11 adds 3-4 days). Other waves run in parallel where dependencies allow.

> **Wave 11 added 2026-06-29 (post-Wave-10-task-100 UAT discovery).** Wave 10 task 100 marked 15 success criteria GREEN at the verification report level, but Wave 10 task 101 (`/narrate` UAT — R4 graduation gate per FR-15) cannot pass because the `PlaybookOrchestrationService` template engine only does literal `{{paramName}}` substitution — it does NOT carry node outputs forward as resolvable context, lacks the custom helpers (`{{json}}`, `{{map}}`, `{{flatten}}`, `{{distinct}}`, `{{concat}}`, `{{join}}`), and lacks fan-out iteration semantics. The deployed `DAILY-BRIEFING-NARRATE` playbook uses all of these. So `/narrate` returns HTTP 200 but with empty `summary` / `keyTakeaways[]` / `channelNarratives[]`. Wave 11 closes that gap. **Until Wave 11 + UAT pass, R7 cannot ship — there is no "closure" without passing UAT.**

### Dependencies (inter-wave)

| Dependency | Blocks until done |
|---|---|
| Wave 1 (AiCompletionNodeExecutor compiles + DI registered) | Wave 2 enum rename (avoids merge conflicts) |
| Wave 2 (`sprk_executortype` reads canonical in orchestrator) | Wave 5 (backfill writes values for orchestrator to read) |
| Wave 5 (94 nodes populated) | Wave 11 + Wave 10 UAT (no null `sprk_executortype` in spaarkedev1) |
| Wave 11 (orchestrator runtime variable resolution + UAT drive) | Wave 10 task 101 (UAT closure) + `090-project-wrap-up` |
| Wave 4 (legacy `ExecuteAnalysisAsync` deleted) | Wave 9 (chat-summarize migration must precede deletion) — note: order Wave 9 before Wave 4 |

## Work Breakdown Structure (WBS)

### Wave 1 — AiCompletionNodeExecutor build (FR-12 to FR-15)

Goal: close R4 `/narrate` gap. Estimated ~10 tasks, 2-3 days.

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 001 | Audit existing AiAnalysisNodeExecutor + EntityNameValidatorNodeExecutor for AiCompletion patterns | spec FR-12, `Services/Ai/Nodes/*.cs` | Pattern decision doc in `notes/spikes/` | — |
| 002 | Scaffold `AiCompletionNodeExecutor.cs` (interface impl, constructor, validation) | 001 | `Services/Ai/Nodes/AiCompletionNodeExecutor.cs` (compile-clean) | 001 |
| 003 | Implement payload binding + PromptSchemaOverrideMerger integration | 002, FR-12 | Executor reads Action SystemPrompt + applies node overrides | 002 |
| 004 | Implement IOpenAiClient.GetStructuredCompletionRawAsync call + JsonElement binding | 003 | Executor returns JsonElement to node.OutputVariable | 003 |
| 005 | Implement Validate() — Action FK required; Tool + Document NOT required (FR-13) | 002, FR-13 | NodeValidationResult fast-fails on bad input | 002 |
| 006 | Register AiCompletionNodeExecutor as Singleton in AnalysisServicesModule.AddNodeExecutors | 003-005, ADR-010 | DI registration; build passes | 003 |
| 007 | xUnit tests — payload binding + schema rendering + template substitution | 003, FR-14 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/AiCompletionNodeExecutorTests.cs` | 003 |
| 008 | xUnit tests — temperature override + per-node prompt override | 003, FR-14 | Tests added | 007 |
| 009 | xUnit tests — error paths (missing prompt, malformed JSON, LLM error) | 005, FR-14 | Tests added; coverage >85% | 007 |
| 010 | Wave 1 BFF publish + size check (NFR-01) | 006 | Publish artifact ≤ +2 MB; CVE scan clean | 006 |

### Wave 2 — Dispatch refactor + enum rename (FR-07 to FR-10)

Goal: collapse 3-layer dispatch to single-hop `node.sprk_executortype` read. Estimated ~18 tasks, 3-4 days.

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 020 | Audit all `ActionType` references via grep (~1000+) | spec FR-10 | Audit report in `notes/spikes/`; counts by file | — |
| 021 | Plan rename strategy (PR sizing, conflict-risk windows) | 020 | Rename plan in `notes/spikes/` | 020 |
| 022 | Rename C# enum `ActionType` → `ExecutorType` (full refactor, BFF only) | 020, 021 | Build passes; tests pass; no behavior change | 010 (W1 complete) |
| 023 | Rename `INodeExecutor.SupportedActionTypes` → `SupportedExecutorTypes` | 022, FR-10 | Interface + all impls renamed | 022 |
| 024 | Update `PlaybookOrchestrationService.ExecuteNodeAsync` to read `node.sprk_executortype` single-hop (FR-07) | 022, FR-07 | Dispatch single-hop; unit test confirms no fallback | 022 |
| 025 | Delete structural fallback ladder (`IsDeployedStartNode`, `IsDeployedLoadKnowledgeNode`, `IsDeployedReturnResponseNode`, `ExtractActionTypeFromConfig`) — ~150 LOC delete (FR-08) | 024 | Code removed; build + tests pass | 024 |
| 026 | Delete "Action ActionType is canonical regardless of NodeType" override branch at lines 1241-1278 (FR-09) | 024 | Code removed; Insights pipeline integration tests pass | 024 |
| 027 | Update `NodeExecutorRegistry` dispatch to use `ExecutorType` | 022, 023 | Registry refactored | 023 |
| 028 | Update `AnalysisActionService` Action read path (no ActionType from lookup) | 024 | Action read simplified | 024 |
| 029 | Wave 2 BFF publish + size check (NFR-01) | 022-028 | Publish artifact OK; CVE scan clean | 028 |

### Wave 3 — Typed config schemas (FR-16)

Goal: each executor declares config shape; BFF serves schemas to PlaybookBuilder. Estimated ~8 tasks, 2 days. Can start after Wave 1.

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 030 | Design `INodeExecutor.GetConfigSchema()` signature + schema DTO shape | FR-16 | Design doc in `notes/spikes/` | — |
| 031 | Add `GetConfigSchema()` to `INodeExecutor` interface | 030 | Interface updated; build passes | 030 |
| 032 | Implement `GetConfigSchema()` on all 33 executors (initial: ~5 with rich schemas, remainder with placeholder schemas) | 031, FR-16 | All executors return schemas | 031 |
| 033 | Implement BFF endpoint `GET /api/ai/playbook-builder/executor-config-schemas` | 032, FR-16 | Endpoint serves full registry | 032 |
| 034 | xUnit tests for endpoint + schema serialization | 033 | Tests pass | 033 |
| 035 | Document schema shape in `docs/architecture/AI-ARCHITECTURE.md` | 030 | Doc updated | 030 |
| 036 | Wave 3 BFF publish + size check (NFR-01) | 033, 034 | Publish artifact OK | 034 |

### Wave 4 — Schema cleanup + remove legacy direct-path (FR-03, FR-04, FR-11)

Goal: delete unused columns + legacy `ExecuteAnalysisAsync`. Estimated ~8 tasks, 1-2 days. Must follow Wave 9 chat-summarize migration.

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 040 | Audit all callers of `AnalysisOrchestrationService.ExecuteAnalysisAsync` | FR-11 | Caller list in `notes/spikes/` | — |
| 041 | Migrate remaining callers (besides chat-summarize handled in W9) to PlaybookOrchestrationService.ExecuteAsync | 040, FR-11 | All callers migrated; tests pass | 040, 091 (W9 done) |
| 042 | DELETE `AnalysisOrchestrationService.ExecuteAnalysisAsync` + cascading dead code (FR-11) | 041 | Method removed; grep zero hits | 041 |
| 043 | Drop `sprk_analysisaction.sprk_actiontypeid` (lookup field) via dataverse-create-schema (FR-03) | FR-03 | Field removed from Dataverse | 042 |
| 044 | Drop `sprk_analysisaction.sprk_executoractiontype` (INT column) (FR-04) | FR-04 | Field removed | 043 |
| 045 | Document `sprk_analysisactiontype` as decorative (FR-05) — add doc note | FR-05 | Doc updated | — |
| 046 | Update `AnalysisActionService` to remove all references to dropped fields | 043, 044 | Service simplified; tests pass | 044 |
| 047 | Wave 4 BFF publish + size check (NFR-01) — expect SHRINK | 042-046 | Publish artifact smaller than baseline | 046 |

### Wave 5 — Existing-playbook backfill (FR-19, FR-20)

Goal: populate `sprk_executortype` on 94 existing nodes; update Deploy-Playbook.ps1. Estimated ~7 tasks, 2-3 days (includes owner-review checkpoint).

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 050 | Author `scripts/dataverse/Review-PlaybookNodes-Dispatch.ps1` — lists every node + current Action + suggested executor type (FR-19) | FR-19, pattern: `Add-EntityNameValidatorNodeTypeOption.ps1` | Review script with dry-run mode | 024 (W2 dispatch ready) |
| 051 | Run review tool against spaarkedev1; produce CSV/table for owner review | 050 | `notes/drafts/playbook-node-review-input.csv` | 050 |
| 052 | **OWNER CHECKPOINT** — owner reviews + sets each value | 051 | `notes/drafts/playbook-node-review-output.csv` (owner-decided values) | 051 |
| 053 | Author `scripts/dataverse/Migrate-PlaybookNodes-to-ExecutorType.ps1` — idempotent + dry-run mode (FR-19) | 052, FR-19 | Migration script | 052 |
| 054 | Run migration script (dry-run → real run); audit post-migration null check | 053 | All 94 nodes populated; audit confirms | 053 |
| 055 | Update `Deploy-Playbook.ps1` to write `node.sprk_executortype` explicitly + lint reject unknown values (FR-20) | FR-20 | Script updated; redeploys clean | 054 |
| 056 | Wave 5 sanity — redeploy 3 representative playbooks (Daily Briefing, Insights, chat) | 055 | Clean redeploys; orchestrator dispatch works | 055 |

### Wave 6 — Documentation deletion + updates (FR-28 to FR-31)

Goal: DELETE outdated R4 canonical-truth sections, UPDATE current sections, CREATE consumer-wiring guide. Estimated ~12 tasks, 2-3 days. Can parallelize with Wave 5-8.

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 060 | Audit `docs/architecture/ai-architecture-playbook-runtime.md` for outdated sections | FR-28 | Section-disposition list in `notes/drafts/` | 024 (dispatch ready) |
| 061 | DELETE §5 action-lookup precedence ladder + structural-fallback section | 060 | Doc updated; no SUPERSEDED markers (NFR-08) | 060 |
| 062 | UPDATE `docs/architecture/ai-architecture-actions-nodes-scopes.md` 4-Home decision tree for new model | FR-28 | Doc rewritten | 024 |
| 063 | UPDATE `docs/guides/ai-guide-playbook-deploy-recipe.md` — remove Control-flow name-detection steps | FR-28 | Doc updated | 055 |
| 064 | UPDATE `.claude/constraints/bff-extensions.md` §G (config boundary) for new model (FR-29) | FR-29 | §G updated | 024 |
| 065 | MAJOR UPDATE `docs/guides/JPS-AUTHORING-GUIDE.md` for node-first dispatch (FR-30) | FR-30 | Guide updated | 024 |
| 066 | MAJOR UPDATE `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` (FR-30) | FR-30 | Guide updated | 024 |
| 067 | CREATE `docs/guides/ai-guide-consumer-wiring.md` — maker tutorial (FR-31) | FR-31 | New guide covering 6 consumers + chat-summarize case | 091 (W9 chat-summarize migration done) |
| 068 | UPDATE root CLAUDE.md system entry-points table if changed | — | If applicable | 067 |
| 069 | Post-audit: grep `docs/` for new "deprecated"/"superseded" instances (NFR-08) | 061-068 | Zero new instances | 068 |

### Wave 7 — Skill rewrites (FR-32, FR-33)

Goal: rewrite jps-* skills for node-first dispatch model. Estimated ~6 tasks, 1-2 days. **MUST be sequential per CLAUDE.md §3 Sub-Agent Write Boundary** (.claude/ files cannot be parallel).

| ID | Title | Inputs | Outputs | Dependencies | parallel-safe |
|---|---|---|---|---|---|
| 070 | REWRITE `.claude/skills/jps-action-create/SKILL.md` (FR-32) | FR-32, 024 | Skill body updated; cites §3.1 WHY | 024 | false |
| 071 | REWRITE `.claude/skills/jps-playbook-design/SKILL.md` (FR-32) | FR-32 | Skill body updated | 070 | false |
| 072 | REWRITE `.claude/skills/jps-playbook-audit/SKILL.md` (FR-32) | FR-32 | Skill body updated | 071 | false |
| 073 | REWRITE `.claude/skills/jps-validate/SKILL.md` (FR-32) | FR-32 | Skill body updated | 072 | false |
| 074 | MINOR UPDATE `.claude/skills/jps-scope-refresh/SKILL.md` (FR-33) | FR-33 | Scope catalog generation accurate | 073 | false |
| 075 | Run `/jps-validate` on representative playbooks to confirm skill bodies are functional | 074 | Validation passes | 074 | false |

### Wave 8 — Playbook Builder UI updates (FR-21 to FR-27)

Goal: replace Node Type with Executor Type Choice + 33-executor categorized selector + typed config forms + Action tab. Estimated ~16 tasks, 3-4 days.

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 080 | Audit PlaybookBuilder canvas state for `sprk_nodetype` references (FR-26) | FR-26 | Reference list in `notes/spikes/` | — |
| 081 | Replace Node Type field with Executor Type Choice selector on Power Apps model-driven form (FR-21) | FR-21 | Form updated | 024 |
| 082 | Update PlaybookBuilder canvas Node Types left panel — 33 categorized entries with tier prefix + description (FR-22) | FR-22 | Panel renders categorized selector | 024 |
| 083 | Wire typed config form renderer driven by schema endpoint (FR-23) | FR-23 | Canvas reads schemas from W3 endpoint; renders typed forms | 033 (W3 endpoint done) |
| 084 | Implement typed config forms for 5 priority executors (AI Analysis, AI Completion, Condition, EntityNameValidator, CreateNotification) (FR-23) | 083 | 5 forms render + validate | 083 |
| 085 | Implement remaining 28 executors with schema (placeholder forms OK; can iterate later) | 084 | All 33 executors have a form | 084 |
| 086 | Promote Action selection to new Action tab (FR-24) | FR-24 | Action tab exists; Overview tab no longer shows Action | 082 |
| 087 | KEEP Prompt tab + per-node override wiring (FR-25) | FR-25 | UAT confirms overrides still apply | 086 |
| 088 | Replace `sprk_nodetype` references in canvas state (FR-26) | 080, FR-26 | grep zero hits | 080 |
| 089 | Handle unknown-executor-type warning state (FR-27) | FR-27 | Invalid types show warning; known types render normally | 085 |
| 089a | UI test pass — Executor Type dropdown + tier grouping render | 082, FR-22 | UI tests pass | 089 |
| 089b | UI test pass — typed config forms for 5 priority executors | 084 | UI tests pass | 089 |
| 089c | UI test pass — ADR-021 dark mode compliance for new UI | 081-088 | Dark mode toggle works; no semantic-token violations | 089 |
| 089d | Deploy PlaybookBuilder Code Page to spaarkedev1 | 089a-c | Deployed; smoke test passes | 089c |

### Wave 9 — Consumer migration (FR-17, FR-18)

Goal: migrate chat-summarize + wire Playbook Library into ≥3 consumer surfaces. Estimated ~7 tasks, 2 days. **Must precede Wave 4** (cannot delete ExecuteAnalysisAsync until chat-summarize migrated).

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 090 | Audit `SessionSummarizeOrchestrator` caller graph; design Path A.5 migration (FR-17) | FR-17 | Migration design in `notes/spikes/` | 024 (dispatch ready) |
| 091 | Migrate `SessionSummarizeOrchestrator` to use `IConsumerRoutingService.ResolveAsync("chat-summarize")` + `IInvokePlaybookAi` (FR-17) | 090, FR-17 | Code refactored; integration test green | 090 |
| 092 | Add `chat-summarize` row to `sprk_playbookconsumer` table | 091 | Row exists in spaarkedev1 | 091 |
| 093 | Audit Playbook Library Code Page modal — current routing state | FR-18 | Audit doc in `notes/spikes/` | — |
| 094 | Wire Library modal into spaarke-ai chat surface (FR-18) | 093, FR-18 | "Browse Playbooks" affordance opens modal | 093 |
| 095 | Wire Library modal into briefing widget (FR-18) | 094 | Affordance wired | 094 |
| 096 | Wire Library modal into one ad-hoc launcher (FR-18) | 095 | Affordance wired (≥3 total surfaces) | 095 |

### Wave 10 — Wrap-up

Goal: graduation criteria verification + lessons learned + project close. Estimated ~3 tasks, 1 day. **Tasks 101 + 090-project-wrap-up now block on Wave 11 completion** (added 2026-06-29).

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 100 | Run end-to-end verification of all 15 success criteria from spec.md | All prior waves | Verification report in `notes/handoffs/graduation-verification.md` | 047, 054, 067, 089d, 096 |
| 101 | UAT — `/narrate` via Daily Briefing widget (R4 graduation gate, FR-15) | 100, **Wave 11 task 117** | Owner sign-off captured | 100, **117** |
| 090-project-wrap-up | Wrap-up — README status → Complete, lessons-learned.md, archive project artifacts | 101, **Wave 11 task 119** | Project closed | 101, **119** |

### Wave 11 — Playbook Orchestrator Runtime Variable Resolution + R7 UAT Drive

> Added 2026-06-29 after Wave 10 task 100 marked verification GREEN but Wave 10 task 101 (UAT) discovered the orchestrator template-resolution gap — the actual root cause of empty `/narrate` responses end-to-end. R7 cannot "close" without UAT passing; Wave 11 drives toward that.

**Goal**: wire existing `ITemplateEngine` (Handlebars.NET, `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs`) into `PlaybookOrchestrationService.ApplyConfigJsonTemplates`; carry node outputs forward as resolvable context to subsequent nodes via `RunContext.NodeOutputs`; add the custom helpers (`{{json}}`, `{{map}}`, `{{flatten}}`, `{{distinct}}`, `{{concat}}`, `{{join}}`, `{{flatMap}}` — last replaces inline `lambda` per T113 decision) the playbook source expressions use; implement fan-out iteration semantics (`iteration.iterateOver` + `itemAlias`); restore source-correct `ValidateEntityNames` node configJson (replace smoke-test PATCH from 2026-06-29 R7 hotfix); smoke `/narrate` + drive Daily Briefing widget UAT to passing; address other operator-flagged UAT issues.

**Estimated**: ~10 tasks, 3-4 days. Critical path linear: 110 → 111 → 112+113+114 (parallel) → 115 → 116 → 117 → 118 → 119.

| ID | Title | Inputs | Outputs | Dependencies |
|---|---|---|---|---|
| 110 | Audit current orchestrator template resolution + design `RunContext.NodeOutputs` surface | `PlaybookOrchestrationService.cs`, `RunContext.cs`, `ITemplateEngine.cs`, `TemplateEngine.cs`, `daily-briefing-narrate.json` | `notes/spikes/wave11-orchestrator-resolution-design.md` | — |
| 111 | Wire `ITemplateEngine` into `PlaybookOrchestrationService.ApplyConfigJsonTemplates` + extend PromptSchemaRenderer with structured `## Input` section (Option B; finalized 2026-06-29 after deeper audit) | 110 design (incl. Option B revision), existing orchestrator tests | Layer 1 (orchestrator template resolution) + Layer 2 (PromptSchemaRenderer Input section); 4 new test files; AI executors decoupled from data shape | 110 |
| 111a | Document the Playbook-driven LLM Output pattern (architecture doc + maker guide + cross-links) — binding operator requirement 2026-06-29: "we will need it for Insights Engine and many other areas" | 111 shipped code | `docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`, `docs/guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md`, root CLAUDE.md §17 row, cross-links from 4 existing AI architecture docs | 111 |
| 112 | Register custom Handlebars helpers: `{{json X}}`, `{{map COLL 'field'}}`, `{{flatten X}}`, `{{distinct X}}`, `{{concat A B …}}`, `{{join SEP A B …}}` | 111, `TemplateEngine.cs` | `TemplateEngine.cs` helper registrations + per-helper xUnit tests | 111 |
| 113 | Eliminate `{{lambda}}` from playbook source by adding `{{flatMap COLL 'nested.path'}}` helper + rewriting `ValidateEntityNames.allowList` expression in source | 111, `daily-briefing-narrate.json`, `brief-validate-entity-names.action.json` | `TemplateEngine.cs` `flatMap` helper + source playbook JSON edit + unit tests | 111 |
| 114 | Implement fan-out iteration semantics in `PlaybookOrchestrationService` — when node configJson has `iteration.iterateOver` + `iteration.itemAlias`, orchestrator runs executor N times, binds alias per-iteration, collects outputs as array | 111, ADR-037, INodeExecutor interface | Orchestrator iteration semantics + unit tests with mock executor | 111 |
| 115 | Restore source-correct `ValidateEntityNames` node configJson (replace smoke-test PATCH); author `scripts/dataverse/Sync-DailyBriefingNarratePlaybookNodes.ps1` (pattern: `Sync-BriefNarrateOutputSchemas.ps1`) so source config can be re-applied to spaarkedev1 | 112, 113, 114; `daily-briefing-narrate.json`; existing sync-script pattern | New sync script + deployed `sprk_playbooknode` PATCH on `ValidateEntityNames` | 112, 113, 114 |
| 116 | Build BFF + deploy via `bff-deploy`; smoke `/narrate` via direct curl with realistic payload; expect non-empty `summary` + `keyTakeaways[]` + `channelNarratives[]` | 115; `bff-deploy` skill | `notes/handoffs/wave11-narrate-smoke.md` (HTTP 200 + non-empty content evidence) | 115 |
| 117 | UAT — Daily Briefing widget renders TL;DR + per-channel narratives with real spaarkedev1 notification data; closes FR-15 / R4 graduation gate | 116; widget at `src/client/shared/Spaarke.DailyBriefing.Components/` | Owner sign-off + `notes/handoffs/wave11-uat-r4-graduation.md` | 116 |
| 118 | Address operator-flagged UAT issues from 2026-06-29: "not seeing any events", "links / tools do not work", "two I don't know where those are coming from". Step 1 = investigate + characterize each. If findings interrelate → one fix-task. If independent → spawn T118a / T118b / T118c. | 117; operator UAT feedback | `notes/handoffs/wave11-other-uat-resolutions.md` + zero-or-more sub-tasks | 117 |
| 119 | Wave 11 BFF publish + size check (NFR-01) + CVE scan (NFR-02) | 111-118 | `notes/handoffs/wave11-publish-report.md` (compressed size ≤ 60 MB ceiling, ≤ +2 MB single-task delta vs Wave 5 baseline; 0 new HIGH CVE) | 118 |

## Dependencies

### External

- Power Platform Maker portal (owner manual operations) for any remaining Choice/Choice-set/Lookup edits
- Azure OpenAI deployment for AiCompletion calls (spaarkedev1)
- `docs/architecture/ai-architecture-consumer-routing.md` updates — owned by chat-routing-redesign-r1 (R7 references current version only)

### Internal

- R4 worktree holds open until R7 ships (Wave 10 graduation gate)
- Action Engine R1 holds at Phase 0 spike until R7 ships
- Existing `IInvokePlaybookAi`, `IConsumerRoutingService`, `IOpenAiClient`, `PromptSchemaOverrideMerger` — reused as-is
- `EntityNameValidatorNodeExecutor` — sibling-pattern reference for AiCompletionNodeExecutor structure

## Testing Strategy

- **Unit tests** (xUnit): ≥85% line coverage for AiCompletionNodeExecutor + dispatch path changes (NFR-05). Pattern: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/AiCompletionNodeExecutorTests.cs`. Mock `IOpenAiClient`.
- **Integration tests**: Daily Briefing `/narrate` (FR-15), chat-summarize migration (FR-17), Insights pipeline regression sweep
- **Migration tests**: idempotent rerun confirms no row corruption; dry-run mode confirms preview before applying
- **E2E / UAT**: spaarkedev1 UAT covers all 15 success criteria; owner sign-off gates ship
- **UI tests**: PlaybookBuilder Code Page — Executor Type dropdown render + 5 typed config forms + ADR-021 dark mode (Wave 8 tasks 089a-c)
- **Test diet** at wrap-up: `090-project-wrap-up` invokes `/test-diet` (binding per CLAUDE.md §7) to reconcile tests added during project against ADR-038 build-vs-maintain criteria

## Acceptance Criteria

See [README.md Graduation Criteria](./README.md#graduation-criteria) and [spec.md §Success Criteria](./spec.md). Verification covered by Wave 10 tasks 100, 101, 090-project-wrap-up.

## Risk Register

See [README.md Risks & Mitigations](./README.md#risks--mitigations). Additional execution-time risks:

| Risk | Mitigation |
|---|---|
| Wave 2 enum rename creates large diff that blocks parallel work | Schedule rename early in Wave 2 (task 022); hold sibling worktrees per spec coordination |
| Wave 5 owner-review checkpoint delays critical path | Surface checkpoint early; provide review tool output in CSV for owner workflow |
| Wave 8 PlaybookBuilder UI changes regress Power Apps form | Test deploy to spaarkedev1 dev before test/prod; capture before/after screenshots |
| Wave 4 schema field drop blocks if any code still reads dropped fields | Wave 4 must follow Wave 9 (chat-summarize migration). Dependency enforced in WBS. |
| **Wave 11**: Handlebars subexpressions can't express the playbook's nested `{{distinct (concat (map …))}}` cleanly | T113 explicitly biases toward rewriting the source `allowList` expression using a new `{{flatMap COLL 'nested.path'}}` helper rather than registering a true lambda — simpler engine, cleaner source |
| **Wave 11**: Fan-out iteration (per-iteration alias binding + output-collection-as-array) not natively in Handlebars `#each` | T114 implements iteration AS ORCHESTRATOR LOGIC (not as a Handlebars helper); engine resolves per-iteration context overlays |
| **Wave 11**: T118 may surface deeper gaps requiring additional R7 work | T118 produces investigation findings; if a finding requires >1 day of work, file as DEF-NNN via `/project-defer-issue-tracking` and surface to operator; do not silently extend Wave 11 |
| **Wave 11**: Restoring source-correct ValidateEntityNames config breaks the smoke-test path I PATCHed 2026-06-29 | T115 only proceeds after T112 + T113 + T114 are merged + tested; sync script is dry-run-first; smoke at T116 confirms before T117 UAT |

## Parallel Execution Groups

Tasks within a group can run concurrently; dependencies between groups enforce ordering.

| Group | Tasks | Prerequisite | Parallel-safe |
|---|---|---|---|
| W1-A | 001 | — | yes |
| W1-B | 002 | 001 done | yes |
| W1-C | 003, 005 | 002 done | yes |
| W1-D | 004, 006 | 003 done | yes |
| W1-E | 007, 008, 009 | 003 done | yes |
| W1-F | 010 | 006, 009 done | yes |
| W2-A | 020 | W1 complete | yes |
| W2-B | 021 | 020 done | yes |
| W2-C | 022 | 021 done | yes (single sequential task — large diff) |
| W2-D | 023, 024 | 022 done | yes |
| W2-E | 025, 026, 027, 028 | 024 done | yes |
| W2-F | 029 | 028 done | yes |
| W3 | 030-036 | W1 done | yes (independent of W2) |
| W7 | 070, 071, 072, 073, 074, 075 | 024 done | **NO** (each .claude/ skill rewrite must be sequential per Sub-Agent Write Boundary) |
| W4 | 040-047 | W9 091 done + W2 done | yes within group |
| W5 | 050-056 | W2 done | sequential within group (owner checkpoint) |
| W6 | 060-069 | W2 done | yes within group |
| W8 | 080-089d | W2 done + W3 033 done | yes within group |
| W9 | 090-096 | W2 024 done | yes within group |
| W11-A | 110 | W5 + W8 done (orchestrator end-to-end usable) | yes |
| W11-B | 111 | 110 done | yes |
| W11-C | 112, 113, 114 | 111 done | yes (3 in parallel) |
| W11-D | 115 | 112, 113, 114 done | no (touches deployed data — sequential) |
| W11-E | 116 | 115 done | no (deploys + smokes BFF — sequential) |
| W11-F | 117 | 116 done | no (operator UAT — sequential) |
| W11-G | 118 | 117 done | no (operator review — sequential) |
| W11-H | 119 | 118 done | yes |
| W10 | 100 ✅; 101 + 090-project-wrap-up | 100 done + W11 119 done | sequential |

## Next Steps

1. **Operator confirms** Target Date projection (via `/devops-project-sync` or GitHub UI) once they've reviewed this WBS
2. **Operator commits** generated artifacts: `git add projects/spaarke-ai-platform-unification-r7/ && git commit -m "..."`
3. **Begin Wave 1 task 001** by saying "execute task 001" — invokes `task-execute` per CLAUDE.md §4
4. **Wave 2-10 task POMLs** to be generated by `/task-create` per-wave as work advances (foundation + Wave 1 seed POMLs are pre-generated)

---

*Generated 2026-06-28 by `/project-pipeline` Step 2. WBS derived from spec.md FR enumeration + design.md §10 wave structure.*
