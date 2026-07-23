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

---

## RESOLVED 2026-07-10 (freeze lifted) — AI activation DEPLOYED

- ✅ **5 actions** deployed (direct API upsert).
- ✅ **5 bindings** deployed (direct API POST, `sprk_Action@odata.bind`; corrected `…,compose` surfaces; resolved action links; enabled; tooldescriptions present).
- 🔧 **Schema gap found + fixed**: `compose-draft-alternative` uses `sprk_disposition = 100000006` (Compose, per `Binding.cs:150`), but the local optionset `new_sprk_playbookconsumer_sprk_disposition` on dev only had 100000000–100000005. Added option **100000006 "Compose"** via `InsertOptionValue` + `PublishXml`, then the binding POSTed cleanly. **TOOLING-DEBT #3**: `Deploy-AiCatalogSchemaExtensions.ps1` (line ~216, the `sprk_disposition` picklist definition) should be updated to include the Compose=100000006 option so a fresh environment gets it — otherwise this gap recurs on the next env.
- ✅ **Code page redeployed** (`sprk_spaarkeai`, bundle carries task-048 activation wiring + task-072 Q&A; verified `api/ai/capabilities` + `compose_qa_highlight` in the built HTML).
- **BFF catalog cache = 5-min IMemoryCache TTL** → the deployed BFF serves the 5 compose capabilities on the next read; no restart needed.
- **Owner smoke test (definitive proof)**: open Compose → select a clause → the AI toolbar buttons (Explain / Compare / Draft / + overflow) should be ENABLED → click Explain → dispatches via the 046 seam and streams a schema-valid answer. (Give the BFF cache up to 5 min after the binding deploy.)

---

## RESOLVED 2026-07-12 (round-3 deploy) — DEF-11 compose-revise-document SEEDED

- ✅ **1 Action + 1 Binding** seeded to spaarkedev1 via direct Web API — **Action `44b1eb8f-367e-f111-ab0e-7ced8ddc4a05`** + **Binding `b11aaf8b-367e-f111-ab0e-70a8a590c51c`** (disposition 100000006 Compose; surfaces `assistant,compose`; enabled; link verified). NO version suffix.
- **Seed script PERSISTED** (was reconstructed twice from the ephemeral session scratchpad — now checked in): [`../scripts/seed-compose-revise-document.ps1`](../scripts/seed-compose-revise-document.ps1). Self-locating repo root; idempotent (skips existing rows); reads mirror-first sources under `infra/dataverse/`; matches the live sibling `compose-draft-alternative` field posture. **Empirical-repro finding baked in**: `sprk_inputschema` stores the WHOLE input mirror file (root keys `$comment/actionCode/environment/inputSchema`), while `sprk_outputschemajson` stores the BARE schema (the `.outputSchema` node of `action.json`, root keys `$schema/type/additionalProperties/required/…/properties` — NOT the output mirror, which adds `$id`/`title`). Lookup nav prop is **`sprk_Action`** (capital), confirmed by `_sprk_action_value`.
- **Tooling-debt #1/#2 still open** (shared seeders remain drifted) — this persisted per-capability script is the working path until they're rewritten.
- BFF (46.61 MB, hash-verified, /healthz Healthy) + code page `sprk_spaarkeai` (4940 KB, clean vite build) also deployed same day. BFF 5-min IMemoryCache TTL serves the new capability on next read.
