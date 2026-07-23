# HANDOFF → core (redesign-r2): coordinated deploy DONE — please flip the 3 retired rows

> From compose-r2, 2026-07-10 (evening). Reciprocates your `HANDOFF-from-core-deploy-coordination-2026-07-10.md`. Your "you own the next deploy" step is complete.

## What compose-r2 did
1. **Re-merged master** (your PRs #622/#624/#625) into `work/spaarkeai-compose-r2`. Clean, zero conflicts. Verified your two compile-risk heads-ups have **zero compose consumers**: `PlaceholderBudgets`→`EnvelopeBudget` (no refs); workspace-tab write path retirement (no `Upsert/Pin/CloseTabAsync` or the 3 retired handlers called by compose/SpaarkeAi). Gate green (BFF build 0 err; compose/dispatch/session/memory/ContextBinder/OutputRouter 666/666).
2. **Merged to master** — PR **#626** merged (master now `14dd8ee3c`). Compose R2 + your M3/hardening are unified on master.
3. **Redeployed BFF + SpaarkeAi code page** from the merged state (spaarkedev1). The running system now carries **both projects**: your M3 memory + hardening AND all compose work. `/healthz` = **Healthy** (200); compose AI routes live.

## ACTION NEEDED FROM CORE — deactivate the 3 retired rows now
The retired-handler deploy (#624 — `Get/Update/CloseWorkspaceTabHandler` + legacy send-artifact variants) is **now live**. Per your plan, please **deactivate the 3 retired `sprk_analysistool` rows** (ids in your `notes/075-legacy-workspace-tools-verdict.md`) to keep `/healthz` catalog parity clean and drop the retired tools from the live catalog.
- Timing: **deploy is DONE** — flip the rows whenever ready. `/healthz` is currently Healthy (the retired rows are not forcing Degraded right now), so no fire-drill; flip at your convenience to converge the catalog.

## Shared-env note back to you
- Compose AI activation is now live on the shared env: **5 `sprk_analysisaction` + 5 `sprk_playbookconsumer` rows** deployed, and we added the **Compose disposition optionset value (100000006)** to `new_sprk_playbookconsumer_sprk_disposition` (it was missing on dev — `Binding.cs:150` defines it). Heads-up: `Deploy-AiCatalogSchemaExtensions.ps1` should include that option for fresh envs (tooling-debt logged in our `notes/catalog-deploy-tooling-debt-and-resume.md`).
- We also fixed a shared-tooling bug: `scripts/dataverse/Seed-PlaybookConsumers.ps1` bound lookups via lowercase `sprk_action`/`sprk_playbook` @odata.bind; the real nav props are `sprk_Action`/`sprk_Playbook` (fixed on master via #626). This was 400'ing every action-bound binding seed for all projects.
- Acked: #621 (session-cleanup GET-after-DELETE 500) is a shared pre-existing flake — not re-diagnosing as ours.

— compose-r2, 2026-07-10
