# Catalog deploy: tooling-debt findings + AI-activation resume plan

> **2026-07-10.** Discovered while deploying the compose AI catalog (task 047) to make Compose actually surface AI. Both catalog-deploy scripts are drifted from the live spaarkedev1 schema. Captured here so resume (after the dev deploy-freeze) is a known, low-risk sequence.

## Live-env state right now (spaarkedev1)
- ✅ **5 `sprk_analysisaction` rows DEPLOYED** (compose-explain-clause / compare-to-playbook / draft-alternative / summarize-word-changes / defined-terms). Deployed via a direct API upsert (`scratchpad/deploy-compose-actions.ps1`) because `Deploy-AnalysisAction.ps1` is drifted (see below). Action ids captured in git history (commit "5 actions deployed").
- ❌ **0 `sprk_playbookconsumer` (Binding) rows** for compose — the seed 400'd (drift, below). No damage: 400 = rejected, existing rows intact.
- Net: 5 orphan actions (harmless; no binding routes to them yet).

## Tooling-debt #1 — `scripts/Deploy-AnalysisAction.ps1` (action deploy) is STALE
- It requires `{ actions: [{ actionTypeName: "...", ... }] }` and binds `sprk_ActionTypeId@odata.bind`.
- **The live `sprk_analysisaction` table has NO action-type field at all** (no lookup `sprk_ActionTypeId`, no optionset). Verified against an existing row (INS-OBS). The mirror's `actionType: 0` is a dead ExecutorType enum, not a stored field. The "04 - Analysis vs Analyze" ambiguity was a phantom of this dead script.
- The mirror files (`infra/dataverse/actions/*.action.json`) are bare single objects, not `actions[]`.
- **Real action schema (what actually deploys):** `sprk_actioncode` (key), `sprk_name`, `sprk_description`, `sprk_systemprompt`, `sprk_outputschemajson` (compressed), `sprk_temperature`, `sprk_inputschema`. Upsert by `sprk_actioncode`. No action-type.
- **Fix (deferred):** rewrite `Deploy-AnalysisAction.ps1` to consume the bare `*.action.json` mirrors + drop the action-type lookup. Until then, `scratchpad/deploy-compose-actions.ps1` is the working recipe.

## Tooling-debt #2 — `scripts/dataverse/Seed-PlaybookConsumers.ps1` (binding deploy) — FIXED here (validation-pending)
- **Root cause:** it bound the lookups with the wrong nav-property CASING — `sprk_action@odata.bind` / `sprk_playbook@odata.bind`. The real single-valued nav properties are **`sprk_Action`** / **`sprk_Playbook`** (capital) — metadata-confirmed via `EntityDefinitions(...)/ManyToOneRelationships`. Every action-bound binding 400'd; the lone success (`insights-search`) has `actionCode=null` so it skipped the bind.
- **Fixed** (this commit): lines ~321/328 (`@odata.bind`) + ~334 (the null-clear `$ref` path) now use `sprk_Action`/`sprk_Playbook`. **Confirmed-by-metadata but NOT run under the freeze** — validate with `-DiffOnly` then a seed after the freeze.
- This was broken for EVERY project seeding action-bound bindings, not just compose — worth flagging to whoever owns the shared seeder (likely core / redesign-r2).

## RESUME PLAN (after the dev deploy-freeze lifts) — the last mile to "Compose runs AI"
1. **Deploy the 5 bindings.** Preferred: the now-fixed `Seed-PlaybookConsumers.ps1 -DiffOnly` (confirm still exactly 5 net-new) then `-SkipConfirm`. Fallback (self-contained, already dry-run-validated): `scratchpad/deploy-compose-bindings.ps1` (direct POST with `sprk_Action@odata.bind`, resolves actionCode→id, carries the corrected `...,compose` surfaces; draft-alternative disposition 100000006).
2. **Verify catalog:** `capability-discovery?surface=compose` returns 5 (authenticated) OR query `sprk_playbookconsumers` for the 5 with resolved `_sprk_action_value`.
3. **Redeploy the SpaarkeAi code page** (carries the task-048 activation wiring): clear Vite cache → `npm run build` → `Deploy-SpaarkeAi.ps1`.
4. **Smoke test:** open Compose, select a clause, click **Explain** → confirm it dispatches (the 046 seam) and returns a schema-valid result.

After step 4, Compose surfaces AI end-to-end for a user (closes 101 gap 2.2 fully + task 047).
