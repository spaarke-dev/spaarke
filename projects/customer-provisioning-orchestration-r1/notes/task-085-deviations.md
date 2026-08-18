# Task 085 — Deviations

> **Task**: 085-alias-collapse-ai-search-key
> **Date**: 2026-08-17
> **Executor**: Wave-4-085-AiSearchKeyCollapse (Claude Opus 4.7)

---

## Deviation #1 — Owner directive #3 explicit override for THIS task

**Baseline rule** (per spec.md §7.9 + `projects/customer-provisioning-orchestration-r1/CLAUDE.md` "Human Escalation Triggers"):

> Any **live-dev KV drift** encountered while executing Phase G/H (owner directive #3: don't remediate live-dev)

**Dispatch-time override** (2026-08-17):

> Owner explicitly confirmed the fix-it-now interpretation of directive #3 for this task. Directive #3 was aimed at UNRELATED dev-KV drift, not the AI Search key alias-collapse work that the task 084 manifest was explicitly designed to enable. Owner's reasoning: NOT fixing it now = drift gets lost + will be re-perpetuated every time we deploy from a shelf copy. Wave 4C is exactly where audit-and-fix work belongs.

**Effect on task**: PROCEEDED with dev-only live alias deletion + App Service settings migration + Bicep template canonicalization. Documented pre-check evidence per §7.9 BINDING pre-check protocol in `phase-h-alias-collapse-aisearch-2026-08-17.md`.

**Scope reconciliation**: This override applies ONLY to the AI Search key alias-collapse work. Baseline owner directive #3 remains in force for OTHER live-dev drift (SPRK-DEV-DATAVERSE-URL rename, other alias collapses, prod-specific paths, etc.). Future r1 tasks retain the "don't remediate live-dev" default unless similarly overridden per-task.

## Deviation #2 — Manifest declared 3 aliases; only 2 live in dev

**Manifest** (`scripts/canonical-secret-catalog/manifest.yaml:433-436`) declares the AI Search key aliases as:
1. `ai-search-key`
2. `aisearch-admin-key`
3. `AzureAISearchApiKey`

**Actual dev KV state** (2026-08-17 pre-check):
- `ai-search-key` — EXISTS (deleted by this task)
- `aisearch-admin-key` — DOES NOT EXIST (never seeded in dev; nothing to delete)
- `AzureAISearchApiKey` — EXISTS (deleted by this task)

**Effect on task**: only 2 live aliases deleted, not 3. The 3rd alias `aisearch-admin-key` remains in the manifest's aliases list because it IS present in Bicep source templates (byok/main.bicep, model{1,2}*.bicep) that would create it on future customer deployments if not migrated. Those source-code references were migrated to canonical in this same commit, so the alias should never re-appear.

**Manifest reconciliation**: no manifest edit needed. The manifest's aliases list is a superset of live drift + template drift; the collapse task migrates BOTH kinds. Task 086 (IaC alignment) can further trim the manifest's aliases list if desired post-collapse — deferred to task 086.

## Deviation #3 — Dispatch prompt named different aliases than the manifest

**Dispatch prompt** (STEP 1) listed the "3 known aliases per task 084 manifest" as:
- `aisearch-admin-key` (kebab-case)
- `AiSearch-AdminKey` (PascalCase-with-dash)
- `AISearchAdminKey` (SmashedCase)

**Actual manifest** (`scripts/canonical-secret-catalog/manifest.yaml:433-436`):
- `ai-search-key`
- `aisearch-admin-key`
- `AzureAISearchApiKey`

**Effect on task**: task executed against the MANIFEST'S list (per POML mandatory pre-work step 5 "READ the task 084 manifest ... find the AI Search key entry"). Dispatch prompt was a good-faith summary; the two aliases it named that aren't in the manifest (`AiSearch-AdminKey`, `AISearchAdminKey`) were checked via `az keyvault secret list` regex — neither exists in dev KV, so no additional collapse work needed.

## Deviation #4 — Bicep templates in scope; production PS scripts NOT in scope

Dispatch prompt STEP 3 says "For each consumer of an alias name, update reference to canonical". A strict reading would include:
- `scripts/Seed-ProductionKeyVault.ps1:170`
- `scripts/Configure-ProductionAppSettings.ps1:96`

Both use `ai-search-key`. HOWEVER, dispatch scope explicitly says "dev only per POML" — and updating production seeder/configurator scripts affects PRODUCTION provisioning paths (out-of-scope). These are left untouched; migrating them is a separate coordinated task when prod is recommissioned (per r3 handoff: prod is currently decommissioned).

Bicep templates (`infrastructure/bicep/stacks/model*-*.bicep`, `infrastructure/byok/main.bicep`) ARE in scope because they are **new-customer** provisioning templates — the whole point of r1. Updating them ensures new customers land on canonical from day one. This aligns with §7.9 R4 ("no orphan / duplicate secrets") + FR-36.

---

*Deviations logged per POML `<step order="13" name="Deviations">` + root CLAUDE.md task-completion protocol.*
