# Track-B Batch 4 Evidence — Stale catalogs/seeds/docs/scripts + dependency-free remainder

> **Task**: 073 · **Date**: 2026-07-05 · **Rigor**: STANDARD (declared)
> **Protocol**: verify-dead-first on every item (binding lesson from batches 2+3: the inventory is a stale snapshot — batch 2 found AddToAssistantToggle live; batch 3 found SprkChatBridge live). Live references → keep-with-reason.
> **Cross-checked against**: `notes/audit-inputs/SPAARKE-AI-CODE-INVENTORY.md` §5.4 + §9, `SPAARKE-AI-MIGRATION-MAP.md` Track-B start-now list (line 36), `OVERLAY-MATRIX.md` line 58.

## Verdict table

| # | Item | Inventory claim | Verify-first finding | Verdict |
|---|---|---|---|---|
| 1 | `docs/ai-knowledge/catalogs/` scope-model-index twin | §5.4: divergent twin of `.claude/catalogs/` copy | **Already absent.** Commit `fb043944c` (2026-04-05) "refactor: move scope-model-index.json from docs/ai-knowledge to .claude/catalogs" removed it before the worktree branched. Glob `docs/ai-knowledge/**` = zero; repo-wide find shows exactly ONE copy: `.claude/catalogs/scope-model-index.json` (untouched by this task — task 051 owns it, main session). | **ALREADY-DELETED** (inventory stale; O-14 twin overlap is already resolved) |
| 2 | `scripts/seed-data/actions.json` | §5.4: 2026-01 R4 taxonomy, superseded | **LIVE reference chain**: `Deploy-Actions.ps1:17` loads it; `Deploy-Playbooks.ps1:63` loads it; `Deploy-All-AI-SeedData.ps1:60` deploys it; `Load-DemoSampleData.ps1:448` invokes `Deploy-All-AI-SeedData.ps1`, and `Load-DemoSampleData.ps1` is documented live tooling (`docs/guides/PRODUCTION-DEPLOYMENT-GUIDE.md:633,1633` step 22; `SPAARKE-DEPLOYMENT-GUIDE.md:1281,2016`; `scripts/README.md:225`). Deleting breaks a documented production/demo deploy step. | **KEEP-WITH-REASON** — content is stale R4 taxonomy (inventory correct on staleness) but the file is load-bearing in the live demo-data bootstrap chain. Correct fix is regeneration from deployed R7 taxonomy, not deletion. Deferred to task-050 completion audit / catalog-governance (P4-2). |
| 3 | `scripts/seed-data/playbooks.json` | §5.4: 2026-01 R4 taxonomy, superseded | Same live chain as #2: `Deploy-Playbooks.ps1:21`, `Deploy-All-AI-SeedData.ps1:64`, `output-types.json:88-92` deploy-order dependency ("playbooks.json must be deployed first — PB-011 must exist"). | **KEEP-WITH-REASON** — same ruling as #2. |
| 4 | `scripts/Seed-JpsActions.ps1` | §5.4: sources from project-notes dirs, likely broken | **Brokenness CONFIRMED**: sources `projects/ai-json-prompt-schema-system/notes/jps-conversions/` and `projects/jps-server-rollout/notes/jps-conversions/` (script lines 107-126) — both dirs no longer exist (archived as `projects/x-ai-json-prompt-schema-system` / `projects/x-jps-server-rollout`). **BUT live references abound**: 3 active skills instruct running it — `.claude/skills/jps-action-create/SKILL.md:185,199,204,263`, `.claude/skills/jps-validate/SKILL.md:246`, `.claude/skills/jps-playbook-design/SKILL.md:294` — plus `docs/guides/ai-guide-playbook-deploy-recipe.md:36,158,224,250`, `docs/guides/JPS-AUTHORING-GUIDE.md:625,1217`, `docs/architecture/ai-architecture-actions-nodes-scopes.md:218`, `scripts/README.md:178-188`, and active project `ai-spaarke-action-engine-r1` tasks 053/054/055/071. | **KEEP-WITH-REASON** — deleting would leave dangling run-this-script instructions in three ACTIVE skill flows that a sub-agent CANNOT edit (CLAUDE.md §3 write boundary on `.claude/`). Deletion requires a coordinated main-session sweep (skills + 2 guides + architecture doc + scripts/README). Flagged for task-050 audit; the redesign's own architecture doc (`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md:2485`) already earmarks "Seed-JpsActions.ps1 sources delete" as future work. |
| 5a | `docs/data-model/sprk_ERD-ai-analysis-entities.md` | §5.4: shows `sprk_actiontypeid` (dropped in R7) — actively misleading | Referenced only by `docs/data-model/INDEX.md:44` and `docs/data-model/entity-relationship-model.md:462`. No src/scripts/.github references. Deletion ratified NOW per task; replacement authored at P4 by task 052. | **DELETED** (`git rm`) + both index references updated with deletion note → task 052 |
| 5b | `docs/data-model/sprk_ai-analysis-related-entities.md` | §5.4: same 2026-02 stale snapshot | Referenced only by `INDEX.md:45` and `entity-relationship-model.md:463`. | **DELETED** (`git rm`) + index references updated |
| 6 | `FallbackScopeCatalog` / `FallbackPrompts` | §9: "(verify)" | **LIVE — verify resolves to KEEP.** Consumers: `Services/Ai/AiPlaybookBuilderService.cs:224-233` (FallbackPrompts, 10 builder-scope prompt fallbacks), `Services/Ai/Builder/BuilderToolExecutor.cs:954,972,990,1008` (FallbackScopeCatalog.Get{Actions,Skills,Knowledge,Tools}), `Services/Ai/Builder/BuilderAgentService.cs:259-261` (MergeWithFallback of Dataverse catalog). The Playbook Builder surface depends on them when Dataverse catalog rows are missing. None of these files are in agent-020's contention set (SummarizeSessionEndpoint / SessionSummarizeOrchestrator / ActionRunner) — ruling is on liveness, not contention. | **KEEP-WITH-REASON** — live consumers in the Builder surface. §9 "(verify)" flag closes as VERIFIED-LIVE. Left for task-050 audit. |
| 7 | `LoadKnowledgeNodeExecutor` | §9: "R4 placeholder" | **WIRED TO THE FROZEN ENGINE**: DI-registered `Infrastructure/DI/AnalysisServicesModule.cs:1046-1053` as `INodeExecutor` for `ExecutorType.LoadKnowledge = 142` (R4 spaarke-daily-update-service-r4); referenced by `INodeExecutor.cs:291`, `ReturnResponseNodeExecutor.cs:308,316` (shared template-context helper contract), `PlaybookTemplateContextBuilder.cs:11,70` (Wave 11 two-layer template resolution). No longer a bare placeholder — carries Wave-11 integration. | **KEEP-WITH-REASON** — per task constraint "keep-with-reason if wired to the frozen engine". Left for task-050 audit. |

## Out-of-scope observations (recorded, not acted on)

- `docs/data-model/sprk_ERD-diagram-ai-analysis.jpg` — the diagram image of the same misleading 2026-02 ERD. **Zero references repo-wide** (not referenced by the deleted docs, INDEX.md, or anything else — orphan). Not in the batch list, so left in place per the "do not modify files outside the batch list" constraint. Candidate for task 052 (it authors the replacement ERD) or a later Track-B batch.
- `.claude/catalogs/scope-model-index.json` — confirmed the single remaining copy; NOT touched (task 051, main session).
- `scripts/seed-data/` siblings (Deploy-*, Verify-*, Query-*, other JSONs) — same stale-R4 toolchain as items 2-3 but outside batch scope; the keep-with-reason on the JSONs keeps the toolchain coherent until a regeneration project addresses it as a unit.
- Dangling-reference choice for the ERD docs: BOTH reference sites updated in-place (INDEX.md rows kept, struck-through, with deletion note pointing at task 052 — no renumber/restructure; entity-relationship-model.md Related-Documentation rows collapsed to one deletion-note row). Nothing left dangling for task 052 beyond authoring the replacements.

## Grep-zero verification (SHOWN)

```
=== grep: sprk_ERD-ai-analysis-entities / sprk_ai-analysis-related-entities (repo-wide, excl. .git, node_modules, projects/ audit notes) ===
./docs/data-model/INDEX.md:44:| ~~sprk_ERD-ai-analysis-entities.md~~ | AI analysis entity ERD — **deleted 2026-07-05** (stale 2026-02 snapshot showed `sprk_actiontypeid`, dropped in R7; replacement authored by `spaarke-ai-architecture-redesign-r1` task 052 at P4) | 2026-02-13 | — | Deleted |
./docs/data-model/INDEX.md:45:| ~~sprk_ai-analysis-related-entities.md~~ | AI analysis related entities — **deleted 2026-07-05** (stale 2026-02 snapshot; replacement authored by `spaarke-ai-architecture-redesign-r1` task 052 at P4) | 2026-02-13 | — | Deleted |

(Only hits = the two intentional strikethrough deletion-note rows in INDEX.md — no live links remain.
 projects/** survivors are this project's audit-input citations + task POMLs — expected survivors per task step 3.)

=== glob: docs/ai-knowledge anywhere ===
ls: cannot access 'docs/ai-knowledge': No such file or directory

=== scope-model-index copies repo-wide (excl .git/projects) ===
./.claude/catalogs/scope-model-index.json          <- exactly one copy, untouched
```

Git history confirmation for item 1: `fb043944c 2026-04-05 refactor: move scope-model-index.json from docs/ai-knowledge to .claude/catalogs`.

## Build verification

**No build surface touched by this task** — deletions/edits are docs-only (`docs/data-model/`). No `.cs`/`.ts`/`.tsx` modified by task 073; verify-first items 6-7 were ruled keep, so `Services/Ai` is untouched by this batch. Git delta attributable to task 073:

```
 M docs/data-model/INDEX.md
 M docs/data-model/entity-relationship-model.md
 D docs/data-model/sprk_ERD-ai-analysis-entities.md
 D docs/data-model/sprk_ai-analysis-related-entities.md
```

(Working tree also showed `ChatEndpoints.cs` / `ChatSseEventFactory.cs` / `LinearDispatchSseEvent.cs` / `executeLinearDispatch.ts` changes — those belong to parallel agent 020, not this task.)

## ADR-038 test register

No tests deleted or modified in this batch — register empty.

## Acceptance criteria

- [x] Per-item grep-zero for every deleted item outside git history, output SHOWN (FR-TB-01 / NFR-08)
- [x] Builds green or explicit no-code-touched statement with git delta (docs-only; delta shown above)
- [x] `.claude/catalogs/scope-model-index.json` untouched; only the docs/ai-knowledge twin in scope (found already-deleted by fb043944c)
- [x] Verify-first items (`FallbackScopeCatalog`/`FallbackPrompts`, `LoadKnowledgeNodeExecutor`) each carry a ruling with evidence (both KEEP — verified live/wired)
- [x] This evidence file written with per-item table
- [ ] TASK-INDEX update — deferred to MAIN SESSION per parent-agent boundary instruction (sub-agent must not touch TASK-INDEX.md/current-task.md)
