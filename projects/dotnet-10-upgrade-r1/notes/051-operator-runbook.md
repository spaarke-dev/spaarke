# Task 051 — OPERATOR RUNBOOK: no-impact net10 validation on an isolated dev slot

> **You (operator) run this.** Claude cannot: it's a live deploy on the only live environment + a recorded go/no-go.
> **Approach**: net10 goes on a NEW `staging` slot; the main dev slot stays net8, so the other 13 BFF worktrees are unaffected. NO swap in Phase 1. The swap is Phase 2 (coordinated with the master merge).
> **Environment (050 evidence)**: `spaarke-bff-dev` / `rg-spaarke-dev`, P1v3 Linux, West US 2, currently `DOTNETCORE|8.0`, **user-assigned MI** `mi-bff-api-dev` (clientId `5967251e-171c-46fe-a6c2-ef843c90309d`). `DOTNETCORE:10.0` is available in-region.
> **Prereqs**: `az login` (you're already on "Spaarke Devlopment Environment"), .NET 10 SDK installed (this worktree's `global.json` = 10.0.100), run from the worktree root `c:/code_files/spaarke-wt-dotnet-10-upgrade-r1`.

---

## PHASE 1 — validate net10 in isolation (NO impact, do now)

### Why each step

- A new slot **clones the main slot's config** (`--configuration-source spaarke-bff-dev`) → ~200 app settings + secrets + KeyVault refs come along.
- We **attach the same user-assigned MI** to the slot → KeyVault refs, MI→Dataverse, and Graph/EXO resolve as the identity that's already granted (a slot does NOT inherit the MI automatically).
- We set **only the slot** to `DOTNETCORE|10.0` → main dev stays net8.
- We deploy the net10 binary to the slot and smoke the **slot hostname** — **no swap**.

### Step 1 — create the staging slot (clones config)

```bash
az webapp deployment slot create \
  -g rg-spaarke-dev -n spaarke-bff-dev \
  --slot staging \
  --configuration-source spaarke-bff-dev
```
Expect: slot `staging` created. (It starts as a copy of main → currently `DOTNETCORE|8.0`; we fix that in Step 3.)

### ⚠️ Step 2 has TWO parts — both required or the app crashes at startup (exit 134)

A cloned slot resets `keyVaultReferenceIdentity` to `SystemAssigned`, so every `@Microsoft.KeyVault(...)` app setting (Redis, ServiceBus, Graph cert) fails to resolve and the app aborts parsing the literal KV-ref string. You must BOTH attach the MI (2a) AND point `keyVaultReferenceIdentity` at it (2b). *(This is a slot-provisioning artifact, NOT a net10 issue — main dev already has 2b set, so a real swap-cutover doesn't need it.)*

### Step 2a — attach the SAME user-assigned MI to the slot

```bash
az webapp identity assign \
  -g rg-spaarke-dev -n spaarke-bff-dev --slot staging \
  --identities /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/spe-infrastructure-westus2/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-bff-api-dev
```
Expect: the slot now lists `mi-bff-api-dev`. This is what makes MI→Dataverse / Graph / KeyVault work on the slot (same principal `9fd47efb-...` that's already registered as Dataverse Application User + granted Graph roles).

### Step 2b — point keyVaultReferenceIdentity at that MI (from PowerShell — path-safe)

```powershell
$mi = "/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourcegroups/spe-infrastructure-westus2/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-bff-api-dev"
$slotId = "/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-dev/providers/Microsoft.Web/sites/spaarke-bff-dev/slots/staging"
az resource update --ids $slotId --set "properties.keyVaultReferenceIdentity=$mi"
# verify -> should print the MI resource id, NOT 'SystemAssigned':
az webapp show -g rg-spaarke-dev -n spaarke-bff-dev --slot staging --query keyVaultReferenceIdentity -o tsv
```
> `az webapp update --set keyVaultReferenceIdentity=...` returns **Bad Request** — use `az resource update --ids` as above. Run from **bash** and the leading-slash ids get mangled by Git Bash → run this from **PowerShell**.

### Step 2c — disable the classic App Insights codeless agent (net10 + FR-06)

```powershell
az webapp config appsettings set -g rg-spaarke-dev -n spaarke-bff-dev --slot staging --settings `
  ApplicationInsightsAgent_EXTENSION_VERSION=disabled DiagnosticServices_EXTENSION_VERSION=disabled XDT_MicrosoftApplicationInsights_Mode=disabled
```
FR-06 made OTel→Azure Monitor the sole telemetry path; the codeless `~2` profiler predates net10. OTel (via `APPLICATIONINSIGHTS_CONNECTION_STRING`) is unaffected.

### Step 3 — set the slot runtime to net10 (pipe form, slot ONLY)

```bash
# bash / az CLI:
az webapp config set \
  -g rg-spaarke-dev -n spaarke-bff-dev --slot staging \
  --linux-fx-version "DOTNETCORE|10.0"
```
> **⚠️ PowerShell gotcha**: from `pwsh`, the `|` gets re-parsed as a pipe by cmd.exe when calling `az.cmd`, and the command silently fails (`'10.0' is not recognized...`). Use the **escaped-quote** wrapper in PowerShell:
> ```powershell
> az webapp config set -g rg-spaarke-dev -n spaarke-bff-dev --slot staging --linux-fx-version '"DOTNETCORE|10.0"'
> ```
Verify main is untouched (must still say `DOTNETCORE|8.0`):
```bash
az webapp config show -g rg-spaarke-dev -n spaarke-bff-dev            --query linuxFxVersion -o tsv   # DOTNETCORE|8.0  (main — unchanged)
az webapp config show -g rg-spaarke-dev -n spaarke-bff-dev --slot staging --query linuxFxVersion -o tsv   # DOTNETCORE|10.0 (slot)
```

### Step 4 — publish net10 + deploy to the slot (NO swap)

Either run the helper `pwsh -File projects/dotnet-10-upgrade-r1/notes/deploy-net10-slot-phase1.ps1`
(it does Steps 4a–4c + the health check), **or** run manually:

```powershell
# 4a. fresh framework-dependent linux-x64 publish (~45 MB)
Remove-Item -Recurse -Force deploy/api-publish -ErrorAction SilentlyContinue
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/

# 4b. zip the publish CONTENTS (explicit file list avoids the wildcard bug)
$zip = "deploy/bff-net10-slot.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
$files = Get-ChildItem -LiteralPath (Resolve-Path deploy/api-publish) -Recurse -File
Compress-Archive -Path $files.FullName -DestinationPath $zip -CompressionLevel Optimal

# 4c. deploy to the SLOT (synchronous), NO swap
az webapp deploy -g rg-spaarke-dev -n spaarke-bff-dev --slot staging `
  --type zip --src-path $zip --async false
```
Expect: deploy success. Slot cold-start on Linux can take 90–120 s.

### Step 5 — smoke the SLOT hostname (`...-staging`)

Slot URL: `https://spaarke-bff-dev-staging.azurewebsites.net`

```bash
# 5a. health — MUST be 200 (net10 host started)
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev-staging.azurewebsites.net/healthz

# 5c. MI -> Dataverse (proves the attached MI works on net10). Use a real doc id.
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev-staging.azurewebsites.net/healthz/dataverse/doc/<known-doc-id>

# 5d. watch logs for auth/Graph/EXO 403s and startup exceptions while smoking
az webapp log tail -g rg-spaarke-dev -n spaarke-bff-dev --slot staging
```
**Full smoke checklist**: `docs/guides/auth-deployment-setup.md` §9 — §9a health, §9c MI→Dataverse, §9d EXO mailbox (no 403 in `InboundPollingBackupService` logs). Also confirm the **Graph 6.5 / Kiota 2.0** OBO path (task 033) and **FR-06 telemetry** (OTel→Azure Monitor emitting; classic App Insights SDK removed in task 014).

> **Slot smoke limits**: §9b (browser OBO) and §9e (browser MSAL) require a Spaarke client pointed at the slot hostname; if your clients only target main dev, those two complete **post-swap** on main dev (Phase 2). §9a/§9c/§9d are fully validatable on the slot server-side.

### Step 6 — GO/NO-GO (record it)

- **GO** = slot `/healthz` 200 on net10, MI→Dataverse OK, no auth/Graph/EXO 403s, no startup exceptions, telemetry emitting. → Proceed to Phase 2 (coordinated swap) when ready.
- **NO-GO** = any of the above fails. The slot is isolated — **delete it and nothing was affected**: `az webapp deployment slot delete -g rg-spaarke-dev -n spaarke-bff-dev --slot staging`. Capture logs, report back.

Record the go/no-go verdict + evidence in `notes/051-smoke-result.md`.

---

## PHASE 2 — coordinated cutover (do LATER, with the master merge)

Do NOT swap until (a) net10 is merged to master and (b) the 13 BFF worktrees are told to update. See `notes/051-coordination.md` for the full ordering. The swap itself:

```bash
# atomic: net10 runtime + binary move from slot -> production together
az webapp deployment slot swap -g rg-spaarke-dev -n spaarke-bff-dev --slot staging --target-slot production
# verify main dev now net10
az webapp config show -g rg-spaarke-dev -n spaarke-bff-dev --query linuxFxVersion -o tsv   # DOTNETCORE|10.0
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz  # 200
```

### Rollback = swap back (instant)

```bash
az webapp deployment slot swap -g rg-spaarke-dev -n spaarke-bff-dev --slot staging --target-slot production
# main dev returns to net8 (the old runtime+binary that the swap parked on staging)
```

### Broadcast to the 13 BFF worktrees (send this)

> **dev is going .NET 10.** Before your next BFF build/deploy: (1) install the **.NET 10 SDK** (`global.json` pins 10.0.100 → without it you get NETSDK1045); (2) `git fetch origin && git merge origin/master` in your worktree to pick up net10 (TFMs, package alignment, pin removals). After that, your builds/deploys are net10-compatible with the flipped dev runtime. Questions → see `projects/dotnet-10-upgrade-r1/notes/051-coordination.md`.

---

## Notes / caveats

- **Plan capacity**: P1v3, capacity 1 — the staging slot shares the single instance's compute. Fine for a short validation; don't leave a heavy slot running indefinitely.
- **`linuxFxVersion` swaps with the slot** (it's site config, not slot-sticky) → the net10 runtime follows the net10 binary into production on swap. That's the intended atomic behavior.
- **Master re-sync first**: this branch is ~40 commits behind origin/master again. Before the Phase-2 merge, re-sync master into the branch + re-verify green (that's part of 090 wrap-up / the merge).
- **Functions (insights)**: if deployed via `func publish`, pin Core Tools ≠ v4.7.0 (task 041 caveat; ARM/Bicep unaffected).
