# .NET 10 BFF Deploy Runbook — dev direct-deploy (now) + prod/demo slot-swap (future)

> **Date**: 2026-08-13 · **Task**: 042 (FR-14) · **Executed by**: operator in tasks 050/051 (dev) and 060/061 (prod/demo, deferred).
> **Companion skill**: [`.claude/skills/bff-deploy/SKILL.md`](../../../.claude/skills/bff-deploy/SKILL.md) (hash-verify + health-check mechanics). This runbook adds the **net10 runtime-string** and **slot-swap** layer on top.

---

## Hard facts (bake these in — a mistake here = hard startup 503)

| Fact | Value |
|---|---|
| Runtime string — **pipe** form | `DOTNETCORE|10.0` — for `linuxFxVersion`, `az webapp config set --linux-fx-version`, slot config, Bicep |
| Runtime string — **colon** form | `DOTNETCORE:10.0` — for `az webapp create --runtime`, `az webapp list-runtimes` |
| Mismatch consequence | Runtime string ≠ deployed TFM → **HTTP 503** at startup (host can't load the app) |
| Publish shape | **Framework-dependent linux-x64** from `deploy/api-publish/` (ADR-029). ~45 MB compressed (task 031: 44.96 MB incl. PDBs). |
| Port | App listens on **8080** (App Service injects `ASPNETCORE_URLS`). **Do NOT** hardcode `ASPNETCORE_URLS` or `UseUrls()`. |
| Framework pin | **Do NOT** set `RuntimeFrameworkVersion` — the platform supplies the shared framework via the runtime string. |
| Self-contained | **No.** `SelfContained=false`, no `PublishTrimmed`/`PublishAot` (ADR-029). |
| Linux auto-swap | **Unsupported.** Linux App Service has **no auto-swap**; the swap is manual (`az webapp deployment slot swap`) or pipeline-driven. |
| Rollback | **Swap back.** A slot swap is atomic and reversible — re-run the swap to restore the prior slot (which still runs the old runtime+binary). |

**Environment reality (owner 2026-08-11)**: only `spaarke-bff-dev` (`rg-spaarke-dev`) is live. demo/prod are decommissioned for budget → **Section B is deferred** until they are re-provisioned on net10.

---

## Section A — NEAR-TERM (ACTIVE PATH): direct deploy to `spaarke-bff-dev`

This is the path for tasks 050/051. **Not via CI** — the `push: master` auto-deploy on `deploy-bff-api.yml` was removed 2026-08-11 (it is now `workflow_dispatch`-only). Deploy from the worktree with the hardened script + `az`. Brief dev downtime is acceptable (dev has no production SLA); if `spaarke-bff-dev` has a staging slot (confirm in task 050), prefer the Section-B slot path even for dev.

### A.0 — Pre-flight

```bash
# Confirm the target exists (skill Failure-Modes: names have moved before)
az webapp show -g rg-spaarke-dev -n spaarke-bff-dev --query state -o tsv        # -> "Running"

# Confirm the CURRENT runtime (expect DOTNETCORE|8.0 pre-cutover)
az webapp config show -g rg-spaarke-dev -n spaarke-bff-dev --query linuxFxVersion -o tsv
```

### A.1 — Publish (framework-dependent linux-x64)

```bash
rm -rf deploy/api-publish
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
# Verify ~45 MB compressed, framework-dependent (NO runtimes/ RID tree), no PublishTrimmed/Aot.
```

### A.2 — Set the runtime string to net10 (pipe form)

```bash
az webapp config set -g rg-spaarke-dev -n spaarke-bff-dev \
  --linux-fx-version "DOTNETCORE|10.0"
```
> The deployed binary (net10) and the runtime string (`DOTNETCORE|10.0`) must change **together**. Setting the runtime first then deploying is fine on a single site (brief 503 window until the net10 zip lands); on a slot (Section B) they land together and swap atomically.

### A.3 — Deploy the binary (hardened script; hash-verify + health)

```powershell
pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1
```
The script publishes, zips, `az webapp deploy --type zip`, SHA-256 hash-verifies the critical files against Kudu VFS (catches silent file-lock failures), and polls `/healthz` (up to 120 s for Linux cold start). See the skill for reading its output.

### A.4 — Smoke (per `auth-deployment-setup.md` §9)

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz   # 200
```
Then §9b OBO round-trip (JWT + OBO — exercises the **Graph 6.5 / Kiota 2.0** path landed in task 033), §9c `/healthz/dataverse/doc/{id}` (MI→Dataverse), §9d EXO mailbox (no 403 in `InboundPollingBackupService` logs if Email enabled), §9e browser MSAL regression. Confirm **FR-06 telemetry** (OTel→Azure Monitor is the sole path; classic App Insights SDK removed in task 014) is emitting.

### A.5 — Rollback (dev, single site)

If net10 fails to boot: set the runtime back and redeploy the last-known-good (net8) artifact, OR (preferred if a slot exists) use the Section-B swap-back. On a single site:
```bash
az webapp config set -g rg-spaarke-dev -n spaarke-bff-dev --linux-fx-version "DOTNETCORE|8.0"
# then redeploy the prior net8 zip
```

---

## Section B — FUTURE (DEFERRED): zero-downtime staging-slot swap for prod/demo

Fires only when demo/prod are re-provisioned on net10 (tasks 060/061). Zero-downtime: net10 goes on the **staging slot**, is validated in isolation, then swapped into production atomically. Rollback = swap back.

Assumes production app `<prod-app>` in `<prod-rg>` with a `staging` slot. Substitute real names at execution.

### B.1 — Ensure a staging slot exists (colon form for create/list)

```bash
# List runtimes uses the COLON form
az webapp list-runtimes --os-type linux | grep -i dotnet     # confirm DOTNETCORE:10.0 offered in region

# Create the slot if absent
az webapp deployment slot create -g <prod-rg> -n <prod-app> --slot staging
```

### B.2 — Set the net10 runtime on the SLOT ONLY (pipe form)

```bash
az webapp config set -g <prod-rg> -n <prod-app> --slot staging \
  --linux-fx-version "DOTNETCORE|10.0"
```
> Production slot stays on `DOTNETCORE|8.0` until the swap. The slot carries the net10 runtime + net10 binary together.

### B.3 — Deploy net10 to the SLOT

```bash
rm -rf deploy/api-publish
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
# zip deploy to the slot:
az webapp deploy -g <prod-rg> -n <prod-app> --slot staging --type zip --src-path <publish>.zip
```
(Or the hardened script parameterized for `--slot staging`.) Hash-verify against the **slot's** Kudu VFS.

### B.4 — Validate the slot in isolation

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://<prod-app>-staging.azurewebsites.net/healthz   # 200
```
Run the full §9 smoke against the **slot hostname**. Confirm net10 (`/healthz` + a version endpoint), Graph OBO, MI→Dataverse, telemetry. **Do not swap until the slot is green.**

### B.5 — Swap (atomic; Linux = manual, no auto-swap)

```bash
az webapp deployment slot swap -g <prod-rg> -n <prod-app> --slot staging --target-slot production
```
The runtime string **and** the binary move together in one atomic operation. Production is now net10; the old net8 runtime+binary now sit on the `staging` slot.

### B.6 — Rollback = swap back

```bash
# If production misbehaves after swap, immediately swap back — staging still holds net8.
az webapp deployment slot swap -g <prod-rg> -n <prod-app> --slot staging --target-slot production
```
> Because staging retained the previous production content, one swap-back restores net8 runtime+binary atomically. This is why B.1–B.5 never overwrite the old artifact until after a successful, validated swap. Rehearse the swap-back (task 060) before the real cutover (task 061).

---

## Cross-check with `deploy-bff-api.yml` (task 040)

The pipeline (`workflow_dispatch`-only after 2026-08-11) uses `DOTNET_VERSION: '10.x'` + `setup-dotnet@v6` (task 040) and the same framework-dependent publish. The manual `az` path in this runbook and the pipeline agree on: net10 SDK, framework-dependent linux-x64, `DOTNETCORE|10.0`, no self-contained. Neither auto-fires on merge (owner: no CI-forced BFF deploys).

## Functions (insights) caveat (from task 041)

If the insights Functions app (`dotnet-isolated 10.0`, task 041) is deployed via `func publish`, **pin Azure Functions Core Tools ≠ v4.7.0** ([core-tools#4794](https://github.com/Azure/azure-functions-core-tools/issues/4794) — v4.7.0 regresses net10 Flex publish; v4.6.0 works). Bicep/ARM provisioning is unaffected.
