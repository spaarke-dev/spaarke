# Agent findings — AI manifest / schema / JPS assets (auditor 6/7, 2026-07-05)

Scope: Dataverse AI entity schema docs, JPS assets + schema, `.claude/catalogs`, consumer-routing data,
AI seed/deploy scripts, PlaybookBuilder maker UI. Audited against MASTER (stale `.claude/worktrees` mirrors excluded).

## A1. Dataverse AI entity schema docs
| Item | Status |
|---|---|
| `docs/data-model/sprk-playbookconsumer.md` | **Current** (2026-06-28, chat-routing-redesign-r1) — best-maintained AI schema doc; §3 lists 7 deployed routing rows. |
| `docs/data-model/sprk_ERD-ai-analysis-entities.md` | **STALE + actively misleading** (2026-02-13) — shows `sprk_analysisaction.sprk_actiontypeid` DROPPED in R7; no playbooknode/executortype/playbookconsumer. |
| `docs/data-model/sprk_ai-analysis-related-entities.md` | **STALE** (2026-02-13, 108 KB) — same era. |
| `docs/data-model/INDEX.md` | Index drift — omits sprk-playbookconsumer.md. |
| `docs/data-model/json-field-schemas.md` | 2026-04-05 — not verified against R7 executor configJson shapes. |

**No data-model doc exists for `sprk_playbooknode` or R7-form `sprk_analysisaction`** — node/executor schema
documented only in guides + `executorMetadata.ts`.

## A2. `.claude/catalogs`
- `scope-model-index.json` — **STALE** (`$generated 2026-03-05`): 8 Actions (ACT-001..008), 10 Skills, 10 Knowledge, 8 Tools, 3 Models, 10 PB compositions. Taxonomy CONFLICTS with seed-data (ACT-001 = "Contract Review" here vs "Extract Entities" there). PB-001..010 don't match deployed GUIDs.
- `docs/ai-knowledge/catalogs/scope-model-index.json` — byte-different DUPLICATE (same $generated); `Refresh-ScopeModelIndex.ps1` writes only the `.claude` copy → docs copy silently rots.
- `Refresh-ScopeModelIndex.ps1` not run in ~4 months.

## A3. JPS assets
| Item | Status |
|---|---|
| `docs/guides/JPS-AUTHORING-GUIDE.md` | **Current** — v4.0, R7-rewritten 2026-06-28 (FR-30), node-first. |
| `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` | **Current** — R7-updated 2026-06-28. |
| `docs/guides/ai-guide-playbook-deploy-recipe.md` | **Current** — 2026-06-29 (executorType write path + Lint A). |
| `docs/guides/SCOPE-CONFIGURATION-GUIDE.md` | **Aging** — v1.1 2026-04-05, pre-R7. |
| `infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json` | **Blocked-undeployed** — self-declares `schemaGapBlocker`: `sprk_nodetype` choice lacks `DeliverComposite=100000004`. Authoritative target, not in Dataverse. |
| `infra/dataverse/outputschemas/{matter-prefill,project-prefill,sum-chat-v1}.schema.json` | **Current** — R6 task 034; matter-prefill mirrors `AiPreFillResult` DTO verbatim (NFR-07). Load-bearing. |
| `infra/dataverse/sprk_analysistool-invoke-playbook-row.json` | **Current** — seed row for generic `invoke_playbook` chat tool (R6 P3 task 021). Data-declared generic dispatcher. |
| BFF-embedded playbooks `src/server/api/.../Services/Ai/{Chat,Insights}/Playbooks/*.playbook.json` | Current but **code-tree playbooks, not maker-editable** — parallel manifest surface (summarize-document-for-chat/-workspace, matter-health-single, predict-matter-cost, universal-ingest). |

**No standalone `jps.schema.json`** — the JPS contract lives in the guide prose + PlaybookBuilder TS types
(`types/promptSchema.ts`, `config/promptSchemaTemplates.ts`).

## A4. Consumer-routing data surfaces — FOUR competing routing surfaces
1. **`sprk_playbookconsumer` table** (canonical, maker-editable) — doc §3: 7 rows (matter-pre-fill, project-pre-fill, ai-summary, summarize-file, chat-summarize, email-analysis, daily-briefing-narrate).
2. **`LinearConsumers` appsettings block** — R7 W12: `document-profile` moved HERE (off the table). Per `SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`.
3. **`Workspace.*PlaybookId` appsettings** — the pre-Phase-1R pattern the table was created to REPLACE; still present as empty-string fallbacks → superseded-but-wired.
4. **`Insights.Playbooks.Map` appsettings** — name-keyed, insights-only.

**Consumer count mismatch ×3 artifacts**: doc = 7 rows; `Seed-PlaybookConsumers.ps1` synopsis says 6 (body has 7 incl. `compose-summarize`, but NOT `daily-briefing-narrate`); `ConsumerTypes.cs` = 8 constants (adds `document-profile` which routes via LinearConsumers, not the table). No single artifact agrees.

## A5. Seed / deploy scripts
- Current: `Deploy-Playbook.ps1` (R7 executorType path), R7 migration scripts (`Migrate-PlaybookNodes-to-ExecutorType.ps1`, `Migrate-PlaybookCanvasJson-To-ExecutorType.ps1`, `Drop-Sprk-Analysisaction-{Actiontypeid,Executoractiontype}.ps1`), output-schema migration scripts.
- **Suspect stale**: `Seed-JpsActions.ps1` — sources JPS from project-notes dirs (`projects/ai-json-prompt-schema-system/notes/...`, `projects/jps-server-rollout/notes/...` — latter appears archived). Source paths likely broken.
- **Stale/superseded**: `scripts/seed-data/{actions,playbooks}.json` + Deploy-Actions/Deploy-Playbooks (2026-01-05 R4 era) — divergent taxonomy; PB-006 self-marked superseded.
- Mixed: Deploy-AnalysisAction, Deploy-NotificationPlaybooks, Deploy-R4-Playbook-Nodes (R4-era name), Create-DefaultPlaybook, Create-PlaybookTriggerFields, Seed-PlaybookTriggerMetadata, Index-ExistingPlaybooks.

## A6. PlaybookBuilder maker UI
- `config/executorMetadata.ts` — **Current, authoritative maker vocabulary**: 33-entry EXECUTOR_METADATA, 6 tiers (AI 0-9, Compute 10-19, Mutations 20-29, Control 30-39, Delivery 40-49, Capability 50+). Mirrors server ExecutorType enum. R7 W8 task 082 (FR-22).
- `config/promptSchemaTemplates.ts` — client-side JPS anchor (`spaarke.com/schemas/prompt/v1`).
- Types: promptSchema.ts, scopeTypes.ts, playbook.ts, canvas.ts — **known partial**: `PlaybookNodeType` not widened to 33 (task 088 pending); 20/33 executors bucket to generic `aiAnalysis` renderer.
- `services/executorSchemaService.ts` + stores — dynamic config forms from BFF `GET /api/ai/playbook-builder/executor-config-schemas`.
- `src/solutions/PlaybookLibrary/` (thin), `CopilotAgent/cards/playbook-{library,menu}.json` (adaptive cards; verify GUIDs).

## B0. Manifest vocabulary today
**Maker can declare as data**: playbook (name/type/configJson incl. parameterSchema), nodes (ExecutorType from
33 + actionCode FK + outputVariable + dependsOn + per-node configJson incl. templateParameters/destination/
widgetType/sections), action (JPS systemprompt + outputschemajson), consumer routing row (consumertype→playbook
+ code/env/priority/matchconditions), tools (`sprk_analysistool` handlerclass + jsonschema), scopes/skills/knowledge.
**Still requires code**: executor implementations, ConsumerTypes constants + callers, tool handler classes,
canvas renderers (20/33 fallback), LinearConsumers/Workspace/Insights appsettings routing.

## Key stale/dead list
2026-02 ERD docs (actively misleading), INDEX.md drift, both scope-model-index copies, seed-data R4 taxonomy,
Seed-JpsActions broken sources, Workspace.*PlaybookId block, SCOPE-CONFIGURATION-GUIDE aging, multinode
playbook blocked on option-set gap.

**Live R7-current manifest = three artifacts**: `executorMetadata.ts` (33 executors) + R7-refreshed guides
(JPS/Playbook/Deploy 2026-06-28/29) + `sprk-playbookconsumer.md` (2026-06-28). Everything else pre-R7/stale.
