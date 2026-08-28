---
description: Provision a new Spaarke customer environment end-to-end via L2 control-plane REST API — prereqs, interactive intake, preflight, confirmation gate, execute loop with poll + manual-gate handling, and handoff report
tags: [provisioning, l3-skill, l2-controlplane, deploy, operations, customer-onboarding]
techStack: [powershell, azure-cli, pac-cli, dataverse-mcp, curl, azure]
appliesTo: ["provision-environment", "provision customer", "new customer environment", "customer stamp", "provision {customerId}", "customer-provisioning"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-08-18
---

# Provision Environment

> **Category**: Provisioning
> **Last Reviewed**: 2026-08-18 (customer-provisioning-orchestration-r1 task 075)
> **Reviewed By**: main-session per Sub-Agent Write Boundary (root CLAUDE.md §3) — sub-agents cannot write to `.claude/skills/**`.
> **Procedure**: [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) (operator runbook — §12 interim manual sequence until this skill supersedes)
> **Companion**: [Task 076 Fallback Matrix](#) — added by follow-on task; do not remove the placeholder if empty.

Interactive Claude Code skill for provisioning a **new Spaarke customer environment** (Model 1 shared trial/SMB OR Model 2 dedicated stamp) end-to-end via the **L2 control-plane REST API**. The skill provides the **operator UX layer** — prerequisite checks, intake wizard, preflight, confirmation gate, execute loop with poll + manual-gate handling, and structured handoff report.

**The actual provisioning is performed by**: L2 control-plane (`Sprk.Provisioning.ControlPlane` — enqueues via Service Bus + tracks state in Cosmos + runs `IJobHandler` handlers H0-H14) and its underlying handler catalog. This skill is thin — it drives the operator experience, not the provisioning logic itself.

**Trigger phrases**:
- `/provision-environment {customerId}`
- `/provision-environment` (interactive; prompts for customerId)
- "provision new customer"
- "provision customer {name}"
- "new customer environment"
- "new customer stamp"

---

## Quick Reference

| Item | Value |
|------|-------|
| L2 API base (dev) | `https://spaarke-provisioning-controlplane-dev.azurewebsites.net` |
| L2 API base (prod) | `https://spaarke-provisioning-controlplane-prod.azurewebsites.net` |
| L2 REST surface | `POST /api/runs`, `GET /api/runs/{id}`, `POST /api/runs/{id}/resume`, `POST /api/runs/{id}/clear-quarantine` |
| L2 audience (token) | `api://spaarke.com/provisioning-controlplane-{env}` |
| Operator role required | `Operator` app-role (mutating) OR `Reader` (poll-only) |
| Handler catalog | 20 handlers per run (Model 1 Shared: 19 — skips H0.5; Model 2 Dedicated: 19 — skips H11): H0 / H0.5 / H1 / H2a / H2b / H3 / H4 / H4-shared / H4b / H5 / H6 / H7 / H8 / H9 / H10 / H11 / H12a / H12b / H12c / H13 / H14. Per `HandlerIds.Dispatchable` in `Sprk.Provisioning.ControlPlane.Core` — 21 registered including H0 which is entry-point (not in Dispatchable). See [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §H0–H14. |
| Trap catalog | 6 traps T1-T6 (see design §4B) — each handler asserts its trap clear before reporting success |
| Tenant-isolation invariants | 5 invariants I1-I5 (see design §4D) — asserted by ArchTests + verified at H13 acceptance |
| Estimated wall-clock (Model 2 fresh stamp) | ≤ 1 hour (NFR-03) if no lead-time gates (Azure quota / SPE 24h / customer admin consent) |
| Cost envelope | Model 2 ≤ $400/mo baseline (NFR-04); Model 1 ≤ $430/mo per-customer marginal |
| Handoff report path | `runs/{runId}.md` in operator's cwd (NOT under `.claude/`) |

---

## Critical Rules

### MUST:
- **MUST** run all Step 0 prerequisite checks BEFORE any intake or preflight; any failure is a hard stop
- **MUST** authenticate as the **operator's own AAD identity** (`az login`) — NEVER a service principal (NFR-11 auditable operator action)
- **MUST** call L2 REST API with `Authorization: Bearer <token>` where token is acquired via `az account get-access-token --resource api://spaarke.com/provisioning-controlplane-{env}`
- **MUST** enqueue via L2 (`POST /api/runs`) and poll (`GET /api/runs/{id}`) — NEVER invoke handlers directly or reach into BFF Service Bus
- **MUST** require an **explicit "proceed" phrase** at the Step 3 confirmation gate — a bare "y" or "yes" is INSUFFICIENT
- **MUST** surface manual gates with actionable instructions (URL to click, `az` command to run, etc.) — never fake progress past a gate
- **MUST** produce a handoff report at `runs/{runId}.md` in the operator's working directory on completion (success OR failure)
- **MUST** update `sprk_dataverseenvironment` registry via Dataverse MCP on run completion — fall back per §4.3a.5 if MCP is disconnected
- **MUST** apply canonical KV secret naming per FR-35 pre-check protocol — check LIVE App Service + KV + Dataverse before removing any alias
- **MUST** follow the KV credential-lifecycle rule (updated 2026-08-27 SESSION 13 task 199 for E-3 CLOSED reality per BFF `CLAUDE.md` correction 2026-08-20 + `spaarke-auth-v4-dataverse-MI` task 033 completion 2026-08-24): **`BFF-API-ClientSecret` is GONE** — auth-v4 task 033 deleted BOTH KV copies (`BFF-API-ClientSecret` + `bff-api-client-secret`), all 4 App Service settings, and pinned `Graph:Credentials:Order = [ManagedIdentityFederated]` with `RequireSecretFreeIdentity=true`. Do NOT re-introduce this secret under any name — `CredentialGuardTests` fails the build on any new `.WithClientSecret(...)` site. H4 **omits** `BFF-API-ClientSecret` unconditionally (no sentinel — §9.1 opaque `AADSTS7000215` risk is gone with E-3 closed). Separately, `Dataverse-ClientSecret` never-delete rule STILL in force until 2026-11-23 (auth-v4 owns its retirement). Full rule: [`.claude/constraints/provisioning.md`](../../constraints/provisioning.md) §KV credential lifecycle.

### NEVER:
- **NEVER** skip Step 0 prereqs — they exist because operator machines drift and silent tool-version mismatches cause silent-fail traps
- **NEVER** authenticate as a service principal — the L2 audit trail requires operator identity per NFR-11
- **NEVER** invoke handlers directly (bypassing L2 orchestration) — you'd break the state machine + idempotency + rollback taxonomy
- **NEVER** advance past a `WaitingOnGate` state without the gate actually clearing (verify via Dataverse or Azure state, not by asking the operator "did you do it")
- **NEVER** hardcode a tenant ID — every intake requires explicit `tenantId` (I1 invariant, `Register-EntraAppRegistrations.ps1:63` fix precedent)
- **NEVER** proceed with a `Quarantined` run — quarantine requires explicit `POST /api/runs/{id}/clear-quarantine` with reason + audit trail
- **NEVER** run two concurrent runs against the same `customerId` — L2 returns 409 via optimistic concurrency on `sprk_currentrunid`; respect it
- **NEVER** invent a customerId — must be pre-registered or explicit intake asks operator + validates format

---

## Procedure

### Step 0: Prerequisites Check (Automated) — HARD STOP on any failure

Run every check. Report results as a checklist. Any FAIL is a hard stop — do NOT proceed to Step 1.

#### 0a. Tool version checks

```powershell
pwsh --version               # ≥ 7.4
az --version                 # az-cli ≥ 2.60
pac --version                # ≥ 1.35
git --version                # ≥ 2.40
```

Parse each version; compare against minimum. On mismatch, print the missing/stale tool + install/upgrade instructions.

**COMP-15 addition (SESSION 15 Wave 4) — batch-mode contract test**: when invoked with `--batch`, ALSO verify:
- `az account show` exits 0 within 5s (proves `az login` is live and token cache is not stale). If fails, HARD STOP with: "Batch dispatch requires a fresh `az login` session. Run `az login --scope api://spaarke.com/provisioning-controlplane-$env/.default` interactively before re-invoking."
- Dataverse MCP is contactable via `mcp__dataverse__describe` OR intake explicitly sets `"skipDataverseMcp": true` (deferring registry ops to raw Web API fallback path per Fallback Matrix F1). Silent MCP unavailability in batch mode causes Step 6a registry update to fail silently — the batch-mode audit trail depends on this contract holding at Step 0.

The interactive-mode counterpart of these checks lives in 0b/0d; batch mode reruns them at 0a to fail-fast on subagent environments that lack the necessary tooling.

#### 0b. Operator AAD identity

```powershell
az account show --query "{name:name, tenantId:tenantId, user:user.name}" -o json
az ad signed-in-user show --query "{oid:id, upn:userPrincipalName}" -o json
```

Assertions:
- `tenantId` MUST equal the Spaarke tenant ID (`a221a95e-6abc-4434-aecc-e48338a1b2f2` — verify from environment; fail-fast if mismatched)
- `user.name` MUST be a real UPN (not a service-principal ObjectId)
- If the returned identity is a service principal, HARD STOP with message: "L3 skill requires operator's own AAD identity per NFR-11. Run `az login` interactively. Refusing to proceed under SP auth."

#### 0c. L2 API reachability + Operator role

> **ISH-10 rewrite (SESSION 16)**: earlier drafts of this step POSTed to `/api/runs` with `profile:"dev"` — but `dev` is NOT in the [`intake.schema.json`](../../scripts/provisioning-prereqs/intake.schema.json) profile enum (`spaarke-hosted-model1-trial` / `spaarke-hosted-model2` / `customer-owned-model2`). L2's model-binding would surface a 400 either way, so the probe technically "worked" — but the diagnostic path was wrong: a 400 could mean either "validation failure" (proving auth passed) OR "the probe payload is malformed and we're not actually testing auth." Worse, the probe was a mutating `POST` — even though L2 rejects the row before enqueue, POSTing a garbage payload to a mutation endpoint just to probe role assignment is bad hygiene. The rewrite uses a **read-only `GET`** against a Reader-safe endpoint. If `GET /api/runs/{fake-guid}?customerId=__role-probe__` returns anything OTHER than 403, the operator has at least Reader; a 403 proves the operator has NO role assignment at all.

```powershell
# Acquire token — env is one of {dev, demo, prod}
$env = "dev"  # populated by -Environment CLI arg / intake.controlPlaneEnv / Step 1d prompt (ISH-12 rename SESSION 18)
$token = az account get-access-token `
  --resource "api://spaarke.com/provisioning-controlplane-$env" `
  --query accessToken -o tsv

# Health check L2 (unauth endpoint)
$l2Base = if ($env -eq "prod") { "https://spaarke-provisioning-controlplane-prod.azurewebsites.net" } `
          elseif ($env -eq "demo") { "https://spaarke-provisioning-controlplane-demo.azurewebsites.net" } `
          else { "https://spaarke-provisioning-controlplane-dev.azurewebsites.net" }
curl -sf "$l2Base/healthz"  # expect 200

# --- Role probe — READ-ONLY GET (ISH-10 rewrite; no mutation) ---
# Uses a well-formed but guaranteed-not-to-exist run-id + a valid customerId query param.
# Expected outcomes:
#   200 → run exists (impossible with random probe GUID; treat as noise, retry)
#   404 → route matched, customerId partition check passed, but run not found → PROVES auth+role work (Reader OR Operator both succeed)
#   400 → customerId param missing/malformed (our probe payload bug; fix before shipping)
#   401 → token invalid/expired → re-run `az login` and retry
#   403 → NO role assignment at all → HARD STOP with grant instructions
#   5xx → L2 upstream problem → escalate per Fallback F3
$probeGuid = [Guid]::NewGuid().ToString()
$probeUrl = "$l2Base/api/runs/$probeGuid`?customerId=__role-probe__"
$probeCode = curl -sS -o $null -w "%{http_code}" `
  -H "Authorization: Bearer $token" `
  $probeUrl
switch ($probeCode) {
  '404' { Write-Host "  [PASS] L2 reader/operator role check (HTTP $probeCode on read-probe)" -ForegroundColor Green }
  '200' { Write-Host "  [PASS] L2 reader/operator role check (HTTP $probeCode — probe GUID collision; retrying would return 404)" -ForegroundColor Green }
  '401' { Write-Error "  [FAIL] L2 token rejected (HTTP 401). Run `az login` interactively and retry."; exit 1 }
  '403' { Write-Error "  [FAIL] L2 rejected the operator's identity with HTTP 403 — NO role assignment. Grant the operator's UPN at least the Reader app-role on 'api://spaarke.com/provisioning-controlplane-$env' via Portal or 'az ad app app-role assignment create' (Operator role is required for the actual /provision-environment dispatch — Reader alone will pass this probe but 403 on Step 4 POST)."; exit 1 }
  default { Write-Warning "  [WARN] Unexpected role-probe HTTP $probeCode against $probeUrl — proceeding cautiously; investigate if Step 4 POST returns 403." }
}

# --- Operator-role probe (ISH-10 addendum) — attempt a Reader→Operator distinction ---
# The GET above proves Reader. For Operator, we'd have to POST — but per this section's
# intro we deliberately do NOT probe by POSTing garbage. Operator-role verification
# happens organically at Step 4 (the real POST). A 403 there IS the signal.
Write-Host "  [INFO] Operator-role assignment is verified organically at Step 4 (POST /api/runs). Reader-tier verified here." -ForegroundColor Cyan
```

#### 0d. Dataverse MCP status (optional but strongly recommended)

Attempt an MCP ping (`mcp__dataverse__describe` against a known small table).

**Interactive mode** — if MCP is disconnected, prompt:

```
⚠ Dataverse MCP is not connected.
  Impact: registry updates on run completion will use the fallback matrix
          (pac data / raw Web API PS) — slower but functional.
  Continue anyway? (yes/no)
```

**Batch mode (BAT-04, SESSION 16)** — honor `$script:BatchMcpDisconnectPolicy` bound at Step 1.0:

```powershell
$mcpAlive = $false
try { mcp__dataverse__describe(entityName='sprk_dataverseenvironment') | Out-Null; $mcpAlive = $true } catch { $mcpAlive = $false }

if (-not $mcpAlive) {
  if ($script:SkipInteractiveIntake) {
    # BATCH MODE
    switch ($script:BatchMcpDisconnectPolicy) {
      'failFast' {
        $diag = @{
          check    = 'dataverse-mcp'
          runId    = 'pre-dispatch'
          detected = (Get-Date -Format 'o')
          reason   = 'Dataverse MCP ping returned no result; batch policy mcpDisconnectPolicy=failFast'
          remedy   = 'Reconnect Dataverse MCP (see .claude/skills/provision-environment/SKILL.md Fallback F1) OR rerun with mcpDisconnectPolicy=proceedWithFallback'
        } | ConvertTo-Json -Depth 4
        $diagPath = "runs/pre-dispatch-mcp-disconnect.json"
        New-Item -Path (Split-Path $diagPath) -ItemType Directory -Force | Out-Null
        Set-Content -Path $diagPath -Value $diag
        Write-Error "[skill] Batch HARD STOP (BAT-04, mcpDisconnectPolicy=failFast): Dataverse MCP not reachable. Diagnostic: $diagPath"
        exit 1
      }
      'proceedWithFallback' {
        Write-Warning "[skill] Batch mcpDisconnectPolicy=proceedWithFallback: Dataverse MCP not reachable. Registry ops (Step 1a probe, Step 1f placeholder-create, Step 6a completion PATCH) will use `pac data` / raw Web API fallback per Fallback F1. This choice is captured in Step 7b lessons-learned."
        $script:McpFallbackActive = $true
      }
      default {
        Write-Error "[skill] Batch HARD STOP: unknown mcpDisconnectPolicy '$($script:BatchMcpDisconnectPolicy)'. Valid: failFast | proceedWithFallback."
        exit 1
      }
    }
  } else {
    # INTERACTIVE MODE — the prompt above
    $answer = Read-Host "Continue anyway? (yes/no)"
    if ($answer -ne 'yes') { Write-Error 'Aborted at Step 0d MCP prompt.'; exit 1 }
    $script:McpFallbackActive = $true
  }
} else {
  $script:McpFallbackActive = $false
}
```

MCP status is NOT a hard stop by default in interactive mode; batch mode defaults to `failFast` (per BAT-04 rationale that unattended runs need up-front reliability, not degraded-path surprises later). Either way the fallback matrix handles disconnect (see Fallback Matrix section, added by task 076).

#### 0e. Working directory + git state

```powershell
git rev-parse --show-toplevel   # verify inside a repo (operator's working tree)
git status --porcelain          # note uncommitted changes (informational; not blocking)
```

Runs create `runs/{runId}.md` in the operator's cwd. If cwd is not a git repo, warn: "handoff report will be written to cwd but won't be checkpointed to git — consider running from repo root."

#### 0f. L2 deployment probe (COMP-04 addition SESSION 15 — verifies deployed L2 image is current AND contains H4-shared/H4b)

Before iterating prereqs.yaml or issuing the run POST, verify L2 App Service is:
- Reachable (`az webapp show` state == Running)
- Healthy (`/healthz` returns 200)
- Current image (build-tag assertion — the deployed image must contain the SESSION 15 Wave 2 HANDLER-01 DAG fix; without it, H4-shared/H4b never dispatch and the whole r1 F19/F20 automation is inert on the dispatched run)

```powershell
$l2WebAppName = "spaarke-provisioning-controlplane-$env"
$l2Rg = "rg-spaarke-platform-$env"
$state = az webapp show -g $l2Rg -n $l2WebAppName --query state -o tsv 2>$null
if ($state -ne 'Running') {
  Write-Error "[skill] L2 App Service '$l2WebAppName' is '$state' (expected 'Running'). Deploy L2 before /provision-environment."
  exit 1
}
# /healthz — includes JSON body with build-tag
$health = Invoke-RestMethod -Uri "$l2Base/healthz" -Method GET -TimeoutSec 10 2>$null
$expectedBuildTag = 'SESSION-15-wave-2-handler-01'  # placeholder — replace with real build-tag emit convention when deploy pipeline stamps it
if ($health.buildTag -and $health.buildTag -notmatch $expectedBuildTag) {
  Write-Warning "[skill] L2 build-tag mismatch (got '$($health.buildTag)', expected match on '$expectedBuildTag'). Deployed image may lack the SESSION 15 Wave 2 fixes (HANDLER-01 DAG, REG-01 registry PATCH, ISH-01 tenantId validation). Run may HALT deep in the DAG. Deploy latest L2 image before proceeding."
}
```

Note: build-tag emission from the L2 deploy pipeline is a follow-on — until it lands, this probe is informational (warns on mismatch, does not HARD STOP). Deploy pipeline stamping is tracked separately as a Wave 8-adjacent follow-on.

#### 0g. Report + gate

Present the operator with a summary:

```
PRE-FLIGHT CHECKS
  [PASS] pwsh 7.4.6
  [PASS] az-cli 2.62.0
  [PASS] pac 1.36.3
  [PASS] git 2.42.0
  [PASS] AAD identity: ralph.schroeder@spaarke.com (tenant: a221a95e-...)
  [PASS] L2 API reachable (dev): https://spaarke-provisioning-controlplane-dev.azurewebsites.net
  [PASS] Operator role granted
  [PASS] Dataverse MCP connected
  [PASS] Working directory: c:/code_files/spaarke-wt-customer-provisioning-orchestration-r1

All prerequisite checks passed. Proceeding to intake.
```

If any FAIL: report the failure + resolution instructions + HARD STOP.

---

### Step 0.5: External Prerequisites Iteration (per `scripts/provisioning-prereqs/prereqs.yaml`) — HARD STOP on any failure

Added by `customer-provisioning-orchestration-r1` task 203c per punch-list row A02. Reads the codified [`scripts/provisioning-prereqs/prereqs.yaml`](../../scripts/provisioning-prereqs/prereqs.yaml) manifest and iterates every prereq whose scope is checkable at operator invocation time (`once_per_tenant`, `once_per_subscription`, and — when `-Environment` is known from arg or batch intake — `once_per_env`). Customer-scoped prereqs (`once_per_customer`) defer to Step 2 preflight (server-side L2 H0 handler). This step iterates the manifest DYNAMICALLY — new prereqs added by future task 202 amendments are picked up automatically without a SKILL.md edit.

#### 0.5a. YAML parser + environment fail-fast

```powershell
# One-time install (idempotent); powershell-yaml provides ConvertFrom-Yaml.
if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
  Install-Module powershell-yaml -Scope CurrentUser -Force -Confirm:$false
}
Import-Module powershell-yaml
```

If the module is unavailable AND cannot be installed (offline / restricted-network operator), the operator MUST invoke each prereq check manually per [`docs/guides/PROVISIONING-PREREQUISITES.md`](../../docs/guides/PROVISIONING-PREREQUISITES.md) and pass `-SkipStep0_5` (or `"skipExternalPrereqs": true` in batch intake) to acknowledge the risk. Silent skip is FORBIDDEN.

**COMP-14 environment fail-fast (SESSION 16)** — Step 0.5b's substitution chain and its `$scopesToCheck += 'once_per_env'` branch both require a non-empty `$env`. When `$env` is null/empty, Step 0.5b silently degrades: `once_per_env` prereqs are skipped (invisible to the operator) and every `{env}` token substitutes to the empty string, producing malformed recipes that either fail with cryptic `az` parse errors OR — worse — false-PASS because the resulting name matches nothing.

Different modes have different `$env` timing:
- **Batch mode**: `$env` MUST be set by Step 1.0 (from `intake.controlPlaneEnv` — ISH-12 rename SESSION 18); a null value here means the intake was malformed and never should have passed schema validation, so HARD STOP.
- **Interactive mode**: `$env` is set at Step 1d (after Step 0.5). It is EXPECTED to be null at Step 0.5 time; the `if ($env) { $scopesToCheck += 'once_per_env' }` branch in Step 0.5b handles this by skipping once_per_env prereqs (they get re-checked at Step 2 client-side dry-run once `$env` is known). Emit an INFO message but do NOT fail.

```powershell
if ($script:SkipInteractiveIntake) {
  # BATCH — $env MUST be populated by Step 1.0 from intake.controlPlaneEnv (ISH-12 rename SESSION 18)
  if ([string]::IsNullOrWhiteSpace($env)) {
    Write-Error "[skill-config] Step 0.5a HARD STOP (COMP-14): batch-mode `$env is null/empty after Step 1.0 read of intake.controlPlaneEnv. This means the intake.json passed schema validation with a null/empty controlPlaneEnv field OR the Step 1.0 batch loader dropped it. Correct the intake and rerun. Silent-skip of once_per_env prereqs is FORBIDDEN in batch mode."
    exit 1
  }
  if ($env -notin @('dev','demo','prod')) {
    Write-Error "[skill-config] Step 0.5a HARD STOP (COMP-14): batch-mode `$env='$env' is not one of the valid values (dev|demo|prod) per intake.schema.json. spaarke-constants.yaml per_env_constants.$env lookup would return null; PLX-13 sanity check would emit a confusing 'containerTypeId is null' error. Correct the intake and rerun."
    exit 1
  }
  Write-Host "  [PASS] Batch-mode env='$env' — Step 0.5b will iterate once_per_tenant + once_per_subscription + once_per_env prereqs" -ForegroundColor Green
} else {
  # INTERACTIVE — Step 1d assigns $env; null here is expected and safe
  if ([string]::IsNullOrWhiteSpace($env)) {
    Write-Host "  [INFO] Interactive-mode env not yet assigned (Step 1d has not run); Step 0.5b will skip once_per_env prereqs. They get re-checked at Step 2 client-side dry-run once `$env` is known." -ForegroundColor Cyan
  } elseif ($env -notin @('dev','demo','prod')) {
    Write-Error "[skill-config] Step 0.5a HARD STOP: `$env='$env' is not one of (dev|demo|prod). Correct the CLI arg and rerun."
    exit 1
  }
}
```

#### 0.5b. Iterate the manifest

Per SESSION 15 Wave 4 (SKILL-08 + PLX-01..14 + PRQ-06):
- Substitution block extended from 2 tokens ({env}, {openAiRegion}) to the full set of ~15 tokens the recipes reference. Values are DERIVED (via `az` + Spaarke constants file) rather than hardcoded — this survives per-env drift.
- Author-time regex sanity check (PLX-14): if a recipe references an unresolved `{token}`, the skill emits a targeted `[skill-config]` error identifying the missing substitution BEFORE invoking `bash -c` (turns silent literal-in-cli az errors into loud maintainer diagnostics).
- Defense-in-depth expect-field classifier (PRQ-06): REMOVED. Belt-and-braces was well-intentioned but the belt was broken (only matched FIRST backticked token) and the braces made it worse (false-fails on prose-literal expects like `>= 25600000`). Assertion semantics now live in the recipe itself (per Wave 3 PRQ-03 assertion-recompute + task 206 exit-1 contract). Recipes exit 1 on real failure; classifier trust falls back to exit code.

```powershell
# --- Load Spaarke constants (PLX-13) ---
$constantsPath = Join-Path $repoRoot 'scripts/provisioning-prereqs/spaarke-constants.yaml'
$constants = Get-Content $constantsPath -Raw | ConvertFrom-Yaml

# --- Derive runtime tokens (per PLX-01..07 substitution strategy) ---
$graphAppId       = $constants.microsoft_constants.graphAppId
$subId            = az account show --query id -o tsv
$l2UamiName       = $constants.name_templates.l2UamiName -replace '\{env\}', $env
$platformRg       = $constants.name_templates.platformResourceGroup -replace '\{env\}', $env
$l2UamiJson       = az identity show -g $platformRg -n $l2UamiName -o json | ConvertFrom-Json
$l2UamiPrincipalId = $l2UamiJson.principalId
$l2UamiClientId    = $l2UamiJson.clientId
$l2UamiSpId        = az ad sp show --id $l2UamiClientId --query id -o tsv
$sbNamespace       = $constants.name_templates.sbNamespace -replace '\{env\}', $env
$artifactsStorage  = az storage account show -g $platformRg -n ($constants.name_templates.artifactsStorageName -replace '\{env\}', $env) --query id -o tsv 2>$null
$acrId             = az acr show -g $platformRg -n ($constants.name_templates.acrName -replace '\{env\}', $env) --query id -o tsv 2>$null
$bffAppServiceId   = az webapp list -g $platformRg --query "[?starts_with(name,'sprksharedprod-api') || starts_with(name,'spaarke-bff-$env')].id" -o tsv | Select-Object -First 1
$kvResourceId      = az keyvault show -g $platformRg -n ($constants.name_templates.platformKvName -replace '\{env\}', $env) --query id -o tsv 2>$null
$containerTypeId   = $constants.per_env_constants.$env.containerTypeId
$bffAppId          = $constants.per_env_constants.$env.bffMultiTenantAppId
$adminDvUrl        = $constants.name_templates.registryDvUrl.$env
$openAiRegionResolved = if ($openAiRegion) { $openAiRegion } else { 'westus3' }  # canonical Spaarke split per operator memory

# Sanity: per_env_constants that require operator population MUST be set
if (-not $containerTypeId) {
  Write-Error "[skill-config] scripts/provisioning-prereqs/spaarke-constants.yaml per_env_constants.$env.containerTypeId is null. Operator MUST populate before Step 0.5 iteration. See docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md §2.4 for how to obtain the SPE container-type GUID."
  exit 1
}

$repoRoot = git rev-parse --show-toplevel
$manifestPath = Join-Path $repoRoot 'scripts/provisioning-prereqs/prereqs.yaml'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Yaml

# Determine which scopes are checkable this early
$scopesToCheck = @('once_per_tenant', 'once_per_subscription')
if ($env) { $scopesToCheck += 'once_per_env' }  # $env from arg or batch intake
# Per EXEC-10 / PRQ-05: once_per_customer prereqs are deferred to server-side H0
# (they reference {customerId} which is only known post-intake; scope-mismatch prereqs
# like the deleted PRQ-E-13 have been removed from prereqs.yaml in Wave 3).

$results = @()
foreach ($prereq in $manifest.prereqs) {
  if ($prereq.scope -notin $scopesToCheck) { continue }

  # Full substitution chain (SKILL-08 + PLX-01..10). Missing token → literal-in-cli
  # (caught by the regex sanity check below).
  $recipe = $prereq.check_recipe.cli `
    -replace '\{env\}',                $env `
    -replace '\{openAiRegion\}',       $openAiRegionResolved `
    -replace '\{region\}',             $openAiRegionResolved `
    -replace '\{subId\}',              $subId `
    -replace '\{sub\}',                $subId `
    -replace '\{l2UamiPrincipalId\}',  $l2UamiPrincipalId `
    -replace '\{l2UamiClientId\}',     $l2UamiClientId `
    -replace '\{l2UamiSpId\}',         $l2UamiSpId `
    -replace '\{graphAppId\}',         $graphAppId `
    -replace '\{sbNamespace\}',        $sbNamespace `
    -replace '\{artifactsStorageId\}', $artifactsStorage `
    -replace '\{acrId\}',              $acrId `
    -replace '\{bffAppServiceId\}',    $bffAppServiceId `
    -replace '\{kvResourceId\}',       $kvResourceId `
    -replace '\{containerTypeId\}',    $containerTypeId `
    -replace '\{bffAppId\}',           $bffAppId `
    -replace '\{adminDvUrl\}',         $adminDvUrl

  # --- PLX-14 author-time sanity check ---
  # If any {token} literal survives substitution, the SKILL substitution chain
  # is out of date vs the manifest. Fail LOUD with the offending token instead of
  # invoking bash -c with a corrupt CLI.
  if ($recipe -match '\{[a-zA-Z_][a-zA-Z_0-9]*\}') {
    Write-Error "[skill-config] Recipe for $($prereq.id) references unresolved placeholder '$($Matches[0])'. Extend the substitution block at .claude/skills/provision-environment/SKILL.md § Step 0.5b (currently at ~line 200) with a derivation for this token, or verify it belongs in spaarke-constants.yaml per_env_constants.$env.*."
    $passed = $false
    $output = "[skill-config] unresolved placeholder: $($Matches[0])"
    $results += @{ Id = $prereq.id; Name = $prereq.name; Scope = $prereq.scope; Passed = $passed; ExitCode = -1; Output = $output; Consequence = $prereq.consequence_of_absence; Remediation = $prereq.remediation }
    continue
  }

  Write-Host "  [CHECK] $($prereq.id) $($prereq.name)" -ForegroundColor Yellow

  # Run the recipe via `bash -c` (portable across az CLI + shell for-loops that
  # many recipes use — PRQ-S-03, PRQ-E-06 all include for/if/exit shell syntax
  # that PowerShell's Invoke-Expression does NOT natively handle). Git Bash
  # ships with `git` on Windows; `bash` is native on Linux/macOS.
  #
  # PASS/FAIL SIGNAL IS THE RECIPE'S EXIT CODE (not output shape).
  # Recipes MUST explicitly `exit 1` on any failure condition. Silent empty
  # output no longer implicitly passes — this closed the SESSION 12 gap where
  # PRQ-C-02 (OpenAI model catalog check) silently passed. Wave 3 (SESSION 15)
  # applied the exit-1 contract across every recipe per task 206 + PRQ-03 (each
  # recipe now recomputes its assertion inline).
  $output = & bash -c $recipe 2>&1 | Out-String
  $exitCode = $LASTEXITCODE

  $passed = ($exitCode -eq 0)

  $results += @{
    Id = $prereq.id
    Name = $prereq.name
    Scope = $prereq.scope
    Passed = $passed
    ExitCode = $exitCode
    Output = $output.Trim()
    Consequence = $prereq.consequence_of_absence
    Remediation = $prereq.remediation
  }
}
```

**Recipe author contract** (BINDING for every entry in `prereqs.yaml`):
- Recipe MUST explicitly `exit 1` on any failure condition it detects internally (empty query result, unexpected value, missing account, wrong role, wrong setting, wrong region, wrong pin, etc.). Wave 3 SESSION 15 applied this contract across every recipe per task 206 + PRQ-03.
- Recipe MUST NOT rely on the classifier to interpret empty output as failure. Wave 4 SESSION 15 REMOVED the defense-in-depth expect-field classifier (PRQ-06) — assertion semantics live in the recipe itself; classifier trust falls back to exit code.
- `check_recipe.expect` is a HUMAN-readable description of what success looks like — no longer machine-enforced. Ambiguous prose expects are fine.
- Multi-line shell scripts (`for/if/echo/exit`) are supported natively via the `bash -c` wrapper.
- **Placeholders currently substituted** (SKILL-08 + PLX-01..14 SESSION 15 extension — 17 tokens):
  - Runtime-derived from az: `{subId}`, `{sub}`, `{l2UamiPrincipalId}`, `{l2UamiClientId}`, `{l2UamiSpId}`, `{artifactsStorageId}`, `{acrId}`, `{bffAppServiceId}`, `{kvResourceId}`
  - Interpolated from name_templates: `{sbNamespace}`
  - Loaded from Spaarke constants file: `{graphAppId}` (invariant Microsoft), `{containerTypeId}` + `{bffAppId}` (per_env populated by operator), `{adminDvUrl}` (per_env template)
  - Session/intake variables: `{env}`, `{openAiRegion}`, `{region}` (aliased to openAiRegion)
- **PLX-14 author-time sanity check**: adding a new placeholder to `prereqs.yaml` REQUIRES extending the substitution chain in this section AND (if per_env or invariant) adding to `spaarke-constants.yaml`. If you forget, Step 0.5b emits `[skill-config] unresolved placeholder` and HARD STOPs before invoking bash — targeted diagnostic, no cryptic az CLI parse error.

#### 0.5c. Report + HARD STOP

Present results as a checklist. Any `Passed = $false` triggers HARD STOP with the id + name + recipe output (or exception message) + `consequence_of_absence` + `remediation` link pointing INTO [`docs/guides/PROVISIONING-PREREQUISITES.md`](../../docs/guides/PROVISIONING-PREREQUISITES.md) at the fragment matching the prereq id.

```
EXTERNAL PREREQUISITES (from scripts/provisioning-prereqs/prereqs.yaml)
  [PASS] PRQ-T-01 SPE container-type registered on Spaarke tenant
  [PASS] PRQ-T-02 SPE container-type application permissions granted
  [PASS] PRQ-T-07 Multitenant BFF app-reg (Model 1 tier only)
  [PASS] PRQ-S-01 Azure subscription billing-agreement type known
  [PASS] PRQ-S-02 Azure subscription has a Support Plan (Basic or better)
  [FAIL] PRQ-S-03 Resource-provider registration for required namespaces
    Output:      Microsoft.CognitiveServices=NotRegistered
    Consequence: F6 — az deployment sub create fails on unregistered provider even after az provider register reports success.
    Remediation: az provider register --namespace Microsoft.CognitiveServices, then poll every 30s for 5 min. See docs/guides/PROVISIONING-PREREQUISITES.md#PRQ-S-03.
    HARD STOP — resolve this prereq before proceeding.
```

`-SkipStep0_5` flag bypasses iteration entirely (also settable via `"skipExternalPrereqs": true` in batch intake). Use ONLY when operator has manually verified every applicable prereq. The choice is recorded in Step 7 lessons-learned.md.

---

### Step 1: Interactive Intake

Collect the 4 inputs the L2 REST API requires. If the operator passed `{customerId}` as a slash-command arg, pre-fill it. Otherwise ask.

#### 1.0 Batch mode (`--batch <path.json>`) — added by task 203c per punch-list row A03

For automated / non-interactive invocations. Consumes a JSON intake file validated against [`scripts/provisioning-prereqs/intake.schema.json`](../../scripts/provisioning-prereqs/intake.schema.json) (JSON Schema Draft 2020-12), pre-fills every field in 1a-1f, and skips all interactive prompts.

```powershell
# Skill invoked with --batch flag: $BatchIntakeFile is the JSON path
if ($BatchIntakeFile) {
  $repoRoot = git rev-parse --show-toplevel
  $schemaPath = Join-Path $repoRoot 'scripts/provisioning-prereqs/intake.schema.json'

  # Validate against schema. Preferred: ajv-cli (npm i -g ajv-cli ajv-formats).
  # Fallback: any Draft 2020-12 validator the operator has (e.g., check-jsonschema).
  $validationOutput = & ajv validate `
    --spec draft2020 --strict false `
    -s $schemaPath -d $BatchIntakeFile 2>&1
  if ($LASTEXITCODE -ne 0) {
    Write-Error "Batch intake failed JSON Schema validation ($schemaPath):`n$validationOutput"
    exit 1
  }

  # Pre-fill from validated intake (skips 1a-1e interactive prompts)
  $intake         = Get-Content $BatchIntakeFile -Raw | ConvertFrom-Json -Depth 10
  $customerId     = $intake.customerId
  $tenantId       = $intake.tenantId
  $tenancyModel   = $intake.tenancyModel
  $environment    = $intake.controlPlaneEnv    # ISH-12 rename SESSION 18 — intake field is `controlPlaneEnv`; local var stays `$environment` for existing downstream references
  $env            = $environment                # alias — Step 0.5a fail-fast + Step 0c URL selector read $env
  $profile        = $intake.profile
  $environmentId  = $intake.environmentId       # may be null → 1f auto-creates
  $subscriptionId = $intake.subscriptionId      # ISH-02 — REQUIRED for Model2Dedicated (validated in schema allOf); optional for Model1Shared
  $region         = $intake.region              # optional platform region (default westus2)
  $openAiRegion   = $intake.openAiRegion        # optional AOAI region (default westus3); consumed by Step 4.0 openAiLocation mapping
  $tier           = $intake.tier                # optional
  $estimatedMonthlyUsd = $intake.estimatedMonthlyUsd  # COMP-10 (SESSION 17) + Bucket A HIGH#8 (SESSION 18): consumed by Step 4.0 nonSecretParameters + H0 cost-envelope gate. Null in interactive mode → H0 log-only skips (unchanged interactive behavior).
  $notes          = $intake.notes               # optional
  $operatorUpn    = az ad signed-in-user show --query userPrincipalName -o tsv  # NEVER trust an operatorUpn field in the JSON (would risk NFR-11 spoof)
  $script:SkipInteractiveIntake = $true         # gates 1a-1e prompts below
  $script:SkipStep0_5 = [bool]$intake.skipExternalPrereqs  # honors batch opt-in

  # --- BAT-01/BAT-03 confirmation attestation (SESSION 16) ---
  # Interactive mode requires the literal phrase typed at Step 3.
  # Batch mode requires the same phrase in intake.confirmationAcknowledgment (const in schema).
  # Capture BOTH the phrase AND the intake SHA-256 hash for NFR-11 audit parity.
  if ($intake.confirmationAcknowledgment -ne 'proceed with provisioning') {
    Write-Error "[skill] Batch intake HARD STOP: intake.confirmationAcknowledgment MUST equal the literal 'proceed with provisioning' (batch equivalent of Step 3 interactive gate per wave-0-adr-note Decision 3 / BAT-03). Got: '$($intake.confirmationAcknowledgment)'."
    exit 1
  }
  $confirmationPhrase = $intake.confirmationAcknowledgment
  Write-Host "  [PASS] Confirmation attestation: '$confirmationPhrase' (SHA-256 of intake file captured at Step 4 for audit trail)" -ForegroundColor Green

  # --- BAT-04..09 batch policy fields (SESSION 16 — schema landed Wave 6 commit dc77381f8) ---
  # These are SKILL-LOCAL control-flow policies, NOT L2 payload. Defaults per schema:
  $script:BatchMcpDisconnectPolicy   = if ($intake.mcpDisconnectPolicy)   { $intake.mcpDisconnectPolicy }   else { 'failFast' }             # BAT-04 → Step 0d
  $script:BatchAcknowledgeUpgradeMode = [bool]$intake.acknowledgeUpgradeMode                                                                # BAT-05 → Step 1a
  $script:BatchOnFailedPolicy        = if ($intake.onFailedPolicy)        { $intake.onFailedPolicy }        else { 'abandon' }              # BAT-07 → Step 4b Failed
  $script:BatchOnQuarantinedPolicy   = if ($intake.onQuarantinedPolicy)   { $intake.onQuarantinedPolicy }   else { 'failFast' }             # BAT-07 → Step 4b Quarantined
  $script:BatchOnManualGatePolicy    = if ($intake.onManualGatePolicy)    { $intake.onManualGatePolicy }    else { 'waitAndExit' }          # BAT-08 → Step 5a-d
  $script:BatchCostEnvelopePolicy    = if ($intake.costEnvelopePolicy)    { $intake.costEnvelopePolicy }    else { 'abortOnOverrun' }       # BAT-10 → Step 2 preflight + Step 4b H0 fail-fast
  $script:BatchPostmortemFile        = $intake.postmortemFile                                                                                # BAT-09 → Step 7b

  # Model2Dedicated + costEnvelopePolicy=warnAndProceed is forbidden per schema description
  if ($tenancyModel -eq 'Model2Dedicated' -and $script:BatchCostEnvelopePolicy -eq 'warnAndProceed') {
    Write-Error "[skill] Batch intake HARD STOP: costEnvelopePolicy='warnAndProceed' is FORBIDDEN for Model2Dedicated (per intake.schema.json description; cost envelope MUST abort for prod / customer-owned subs). Change to 'abortOnOverrun' and rerun."
    exit 1
  }

  Write-Host "Batch intake loaded from $BatchIntakeFile (schema-validated + batch policies bound)."
}
```

**Semantics**: when `-BatchIntakeFile` is passed, sub-steps 1a-1e are non-interactive (values already assigned from the validated JSON). Sub-step 1f (environmentId auto-create via Dataverse MCP / `pac data create`) still runs when `intake.environmentId` was omitted or null. The `--batch` path also honors `intake.skipExternalPrereqs` as a batch-native `-SkipStep0_5` equivalent (see Step 0.5c) — recorded in Step 7 lessons-learned.md when set.

Sample intake (see [`intake.schema.json`](../../scripts/provisioning-prereqs/intake.schema.json) `examples` block for full-fidelity sample):

```json
{
  "customerId": "trial1",
  "tenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "tenancyModel": "Model1Shared",
  "controlPlaneEnv": "dev",
  "profile": "spaarke-hosted-model1-trial",
  "region": "westus2",
  "tier": "shared-trial",
  "estimatedMonthlyUsd": 412,
  "confirmationAcknowledgment": "proceed with provisioning",
  "costEnvelopePolicy": "abortOnOverrun"
}
```

The `confirmationAcknowledgment` literal is REQUIRED for batch dispatch (intake.schema.json `const` + top-level `required[]` — Bucket A HIGH#2 SESSION 18); a missing/wrong value hard-stops Step 1.0 (line 515-517). `estimatedMonthlyUsd` + `costEnvelopePolicy` feed the COMP-10 H0 cost-envelope gate end-to-end (Bucket A HIGH#8 SESSION 18); omitting them causes H0 to log-only skip.

Interactive-mode operators skip this section entirely — proceed to 1a.

#### 1a. `customerId` (required)

- Format: `[a-z][a-z0-9-]{2,31}` (kebab-case, 3-32 chars, starts alpha)
- **Uniqueness / upgrade detection** (per Wave 0 Decision 2 / SKILL-02 fix, SESSION 15): probe the `sprk_dataverseenvironment` registry via Dataverse MCP alt-key filter on `sprk_customerid`. Earlier drafts of this skill probed a non-existent `GET /api/runs?customerId=` L2 endpoint (that endpoint has never existed — `RunsEndpoints.cs` maps only 7 routes, none of which is list-by-customerId).

  ```powershell
  # Registry probe — Dataverse MCP alt-key path per ADR-044 canonical registry
  $probe = mcp__dataverse__read_query(query = @"
    <fetch top="1">
      <entity name="sprk_dataverseenvironment">
        <attribute name="sprk_dataverseenvironmentid" />
        <attribute name="sprk_provisionedon" />
        <attribute name="sprk_setupstatus" />
        <filter><condition attribute="sprk_customerid" operator="eq" value="$customerId" /></filter>
      </entity>
    </fetch>
"@)

  if ($probe.rows.Count -eq 0) {
    # Fresh customerId — proceed to Step 1f placeholder-create
    Write-Host "customerId '$customerId' is new (fresh provisioning)"
  } elseif ($probe.rows[0].sprk_provisionedon -ne $null) {
    # Prior successful run — upgrade-mode. BAT-05 branch:
    if ($script:SkipInteractiveIntake) {
      # BATCH MODE — honor $script:BatchAcknowledgeUpgradeMode (SESSION 16)
      if (-not $script:BatchAcknowledgeUpgradeMode) {
        $diag = @{
          check       = 'upgrade-mode-detection'
          customerId  = $customerId
          detected    = (Get-Date -Format 'o')
          reason      = "Prior sprk_provisionedon=$($probe.rows[0].sprk_provisionedon) row exists AND intake.acknowledgeUpgradeMode is false"
          remedy      = "Set intake.acknowledgeUpgradeMode=true if the upgrade path is intended (see design.md §14A upgrade model), OR change customerId to a fresh identifier"
        } | ConvertTo-Json -Depth 4
        $diagPath = "runs/pre-dispatch-upgrade-required.json"
        New-Item -Path (Split-Path $diagPath) -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null
        Set-Content -Path $diagPath -Value $diag
        Write-Error "[skill] Batch HARD STOP (BAT-05): customerId '$customerId' has prior successful provisioning but intake.acknowledgeUpgradeMode is false. Diagnostic: $diagPath"
        exit 1
      }
      Write-Host "  [BATCH] Upgrade-mode acknowledged in intake — proceeding as UPGRADE run against existing environmentId=$($probe.rows[0].sprk_dataverseenvironmentid)"
    } else {
      # INTERACTIVE MODE
      Write-Host "customerId '$customerId' has a prior successful run (sprk_provisionedon=$($probe.rows[0].sprk_provisionedon)). Continue as UPGRADE run? (yes/no)"
      $answer = Read-Host
      if ($answer -ne 'yes') { Write-Error 'Aborted at Step 1a upgrade-mode confirmation prompt.'; exit 1 }
    }
    $environmentId = $probe.rows[0].sprk_dataverseenvironmentid
    $script:IsUpgradeRun = $true
  } else {
    # Prior halt / quarantine / partial (placeholder exists, sprk_provisionedon still null) — recover
    Write-Host "customerId '$customerId' has a prior in-progress row (setupstatus=$($probe.rows[0].sprk_setupstatus)). See Fallback Matrix F1 recovery path."
  }
  ```

  Fallback if Dataverse MCP disconnected: `pac data query --entity sprk_dataverseenvironment --filter "sprk_customerid eq '$customerId'"` OR raw Web API GET with operator's `az` token. See Fallback Matrix F1.
- If reused (upgrade-mode) → per FR-34 §14A upgrade model; operator MUST confirm intent
- If new → this is a fresh-provisioning run (proceed to Step 1f placeholder-create)

#### 1b. `tenantId` (required per I1 invariant — NEVER default)

- Format: RFC 4122 GUID
- The customer's Entra tenant ID (Model 2: their tenant; Model 1: Spaarke's shared tenant)
- Do NOT default; do NOT fall back to `az account show` — the operator MUST supply this explicitly. This enforces the §4D I1 tenant-isolation invariant (FR-28).

#### 1c. `tenancyModel` (required)

Choice:
- `Model1Shared` — shared trial / SMB tier (multi-tenant BFF, shared Dataverse, per-customer container in SPE)
- `Model2Dedicated` — dedicated Azure subscription + dedicated Dataverse env + admin-consent flow required

Explain the trade-off to the operator if they ask.

#### 1d. `environment` (required)

Choice: `dev` / `demo` / `prod` — determines which L2 API base + which Bicep parameter file is used (session-level; NOT the L2 `profile` enum below).

#### 1e. `profile` (required — L2 API enum, per punch list row A09 / DS-5 c6-1)

Choice — MUST match one of these three literal strings exactly (any drift triggers an L2 400 response):

- `spaarke-hosted-model1-trial` — shared trial / SMB (Model 1 shared BFF; per-customer container in SPE; shared Dataverse or per-tenant Dataverse depending on config).
- `spaarke-hosted-model2` — dedicated stamp hosted in Spaarke's subscription (Model 2 with Spaarke as the cloud landlord).
- `customer-owned-model2` — dedicated stamp in the customer's own Azure subscription (Model 2 with customer as landlord + admin-consent flow).

**Reject any other value BEFORE POST /api/runs**. Do not silently substitute or ask the operator to "just try one" — surface the failure with the exact enum choices.

```powershell
$validProfiles = @('spaarke-hosted-model1-trial', 'spaarke-hosted-model2', 'customer-owned-model2')
if ($profile -notin $validProfiles) {
  Write-Error "❌ Invalid profile '$profile'. Must be one of: $($validProfiles -join ', '). Per DS-5 c6-1: L2 API rejects any other value with 400."
  # HARD STOP — do not proceed to Step 2
  exit 1
}
```

Cross-check: `tenancyModel` × `profile` MUST be consistent — `Model1Shared` pairs only with `spaarke-hosted-model1-trial`; `Model2Dedicated` pairs with either `spaarke-hosted-model2` or `customer-owned-model2`. Mismatch → reject before POST.

#### 1f. `environmentId` — create placeholder `sprk_dataverseenvironment` record (required — per punch list rows A10 + A11 / DS-5 c6-2 + c6-3)

The L2 API's `POST /api/runs` REQUIRES `environmentId` (the `sprk_dataverseenvironment` record GUID). L2 returns 400 without it (per DS-5 c6-2). This step creates the placeholder record BEFORE the POST so the GUID is available.

**Registry env (until central-managing env exists — 2026-08-26 owner directive)**: use `spaarkedev1` for `environment=dev`, `spaarke-demo` for `environment=demo`. Production registry env is NOT YET provisioned; treat as an r2 follow-on. The Spaarke dev registry env doubles as the engineering dev env — this is intentional for now. See operator memory `feedback_no_central_managing_env_yet`.

**Preferred path — Dataverse MCP** (`mcp__dataverse__create_record`). The payload MUST include the 4 NOT-NULL fields (`sprk_name`, `sprk_environmenttype`, `sprk_dataverseurl`, `sprk_isactive`, `sprk_isdefault`) OR Dataverse returns 400. Registry columns added in task 023 (deployed 2026-08-26 SESSION 13) + `sprk_customerid` added by companion `scripts/Add-CustomerIdColumn.ps1` (task 199 reconciliation) provide the remaining fields.

```powershell
# environment (intake) -> sprk_environmenttype enum (per DataverseEnvironmentRecord.cs EnvironmentType)
$envTypeMap = @{ 'dev' = 0; 'demo' = 1; 'sandbox' = 2; 'trial' = 3; 'partner' = 4; 'training' = 5; 'prod' = 6 }
$envType    = $envTypeMap[$environment]

# tenancyModel (intake) -> sprk_tenancymodel option-set integer (Model1Shared=0, Model2Dedicated=1)
$tenancyModelMap = @{ 'Model1Shared' = 0; 'Model2Dedicated' = 1 }
$tenancyModelInt = $tenancyModelMap[$tenancyModel]

$placeholderPayload = @{
  entityName = 'sprk_dataverseenvironment'
  attributes = @{
    # --- Required fields (NOT NULL per live schema) ---
    sprk_name             = $customerId                                 # Recommended: customerId doubles as human-readable name; H10 may replace with friendly name at completion
    sprk_environmenttype  = $envType                                    # Choice: enum int per environment
    sprk_dataverseurl     = "https://placeholder-$customerId.crm.dynamics.com"  # H5 promotes this to the real URL when it creates the customer's Dataverse env
    sprk_isactive         = $true
    sprk_isdefault        = $false
    # --- r1 registry extension (task 023 v3.3 columns) ---
    sprk_customerid       = $customerId                                 # ALT-KEY for L2 CustomerRunGuard + DataverseRegistryConcurrencyStore lookup
    sprk_tenantid         = $tenantId
    sprk_tenancymodel     = $tenancyModelInt                            # option-set integer, NOT string
    sprk_setupstatus      = 1                                           # 1=InProgress per EnvironmentSetupStatus enum (NotStarted=0, InProgress=1, Ready=2, Issue=3)
  }
}
$mcpResponse = mcp__dataverse__create_record @placeholderPayload
$environmentId = $mcpResponse.sprk_dataverseenvironmentid
```

Fallback path (per §4.3a.5) — if MCP disconnected, use `pac data create`:

```powershell
$environmentId = pac data create --entity sprk_dataverseenvironment `
  --attributes "sprk_name=$customerId;sprk_environmenttype=$envType;sprk_dataverseurl=https://placeholder-$customerId.crm.dynamics.com;sprk_isactive=true;sprk_isdefault=false;sprk_customerid=$customerId;sprk_tenantid=$tenantId;sprk_tenancymodel=$tenancyModelInt;sprk_setupstatus=1" `
  --query 'sprk_dataverseenvironmentid' -o tsv
```

Verify `$environmentId` is a valid GUID **AND** that the row is queryable via alt-key (PRQ-05 addendum, SESSION 15 — belt-and-suspenders since PRQ-E-13 was deleted from prereqs.yaml; the placeholder-record-exists check now lives here exclusively):

```powershell
if (-not ($environmentId -match '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')) {
  Write-Error "❌ Placeholder create failed — no valid GUID returned. Cannot proceed to POST /api/runs without environmentId. Check registry-env MCP connection or pac data auth."
  exit 1
}

# PRQ-05 post-create verification — round-trip the row via alt-key to prove the
# CustomerRunGuard alt-key lookup path works BEFORE Step 4 POSTs /api/runs
# (avoids discovering the alt-key gap deep inside L2's concurrency-guard
# 409-return path where the diagnostic is much worse).
$verify = mcp__dataverse__read_query(query = @"
  <fetch top="1">
    <entity name="sprk_dataverseenvironment">
      <attribute name="sprk_dataverseenvironmentid" />
      <attribute name="sprk_customerid" />
      <attribute name="sprk_setupstatus" />
      <filter><condition attribute="sprk_customerid" operator="eq" value="$customerId" /></filter>
    </entity>
  </fetch>
"@)
if ($verify.rows.Count -eq 0 -or $verify.rows[0].sprk_dataverseenvironmentid -ne $environmentId) {
  Write-Error "❌ Post-create verification failed — sprk_customerid alt-key returned no row OR returned a different GUID than the create call. CustomerRunGuard concurrency path will 409-loop indefinitely. Check sprk_customerid_key alt-key is registered on the entity (see scripts/Add-CustomerIdColumn.ps1)."
  exit 1
}
```

**Fields intentionally NOT set at placeholder-create time** (populated by later handlers, per task 023 constraint "no code path that reads/writes these columns beyond what already exists in DataverseEnvironmentRecord.cs"):
- `sprk_currentrunid` — set by L2 CustomerRunGuard as part of the run enqueue (§4D I5)
- `sprk_azuresubscriptionid`, `sprk_resourcegroupname`, `sprk_appservicename`, `sprk_keyvaultname`, `sprk_containertypeid` — populated by H2a Bicep composition
- `sprk_bffversion`, `sprk_solutionversion` — populated by H9 BFF deploy + H6 solution import
- `sprk_ClientCacheBustToken` — populated by H7 env-var writes
- `sprk_provisionedon` — set by H13 acceptance gate on transition to Ready
- `sprk_setupstatus = 2 (Ready)` — set by H13 via `DataverseRegistrySetupStatusUpdater` at completion (also clears `sprk_currentrunid`)

The placeholder is later promoted to real state by H10/H13 (setup registry update) when the run reaches `Ready`. This creates the audit trail from "run enqueued" → "run complete" in a single sprk_dataverseenvironment lifecycle.

**Fields deliberately NOT in the placeholder** (deprecated / never landed — do NOT re-add these):
- `sprk_profile` — never authored in any script or code; skill previously referenced it but grep-zero across `src/**` (removed 2026-08-26 SESSION 13 per task 199 reconciliation)
- `sprk_upgrademode` — DERIVED from `sprk_provisionedon IS NOT NULL` by H4 KV secrets handler (`IKvSecretsWriter.UpgradeMode`); never persisted as a column

#### 1g. Show intake summary

```
INTAKE SUMMARY
  customerId:      trial-acme-2026-08-18
  tenantId:        12345678-...-...-...  (customer tenant)
  tenancyModel:    Model1Shared
  controlPlaneEnv: dev
  profile:         spaarke-hosted-model1-trial
  environmentId:   a1b2c3d4-...  (placeholder sprk_dataverseenvironment record, sprk_setupstatus=1 InProgress)
  L2 API:          https://spaarke-provisioning-controlplane-dev.azurewebsites.net

Proceed to preflight (H0)? (yes/no)
```

Wait for "yes" (bare "y" is insufficient at every gate in this skill — spec §4.3a.4).

**BAT-02 (SESSION 16)** — batch mode SKIPS the Step 1g prompt (the summary is written to stdout for audit, but no operator input is expected). The Step 3 confirmation gate is what covers the intent-to-proceed attestation in batch mode (via `intake.confirmationAcknowledgment` const-string validated at Step 1.0). Skipping Step 1g's yes/no prompt in batch mode is NOT a bypass — it eliminates a stdin read that would block the run indefinitely under `--batch` unattended dispatch:

```powershell
if (-not $script:SkipInteractiveIntake) {
  $answer = Read-Host "Proceed to preflight (H0)? (yes/no)"
  if ($answer -ne 'yes') { Write-Error 'Aborted at Step 1g preflight prompt.'; exit 1 }
} else {
  Write-Host "  [BATCH] Step 1g summary printed above; skipping interactive prompt (intent-to-proceed already captured at Step 1.0 via intake.confirmationAcknowledgment)." -ForegroundColor Cyan
}
```

---

### Step 2: Client-side Dry-Run + Preflight Planning (no server mutation)

> **CRITICAL architectural correction (EXEC-02 / SKILL-03 / ISH-03 fix, SESSION 15 Wave 4)**: Step 2 is now CLIENT-SIDE ONLY. Earlier drafts of this skill POSTed to `/api/runs` with a fictional `mode:"preflight"` field — but `CreateRunRequest` (`RunsEndpoints.cs:861-880`) accepts NO `mode` field, silently DROPPED both `tenantId` (I1 invariant violation) and `mode`, and unconditionally enqueued H0 → the full H1..H14 cascade via the reconciler. Step 3's confirmation gate was therefore theatrical: by the time the operator typed "proceed with provisioning," H1-H2a had already fired. The redesign: Step 2 stays client-side (validates + shows plan); Step 3 gate fires BEFORE any L2 POST; Step 4 issues the SINGLE actual POST to `/api/runs`.
>
> **BEFORE this step**, if the target Azure subscription was created within the last 90 days (i.e. "fresh sub"), invoke **Step 2.5 (Fresh-Sub Deployment Feasibility Check)** first. Fresh subs have region/quota/model gotchas that L2's H0 handler does NOT currently check for; skipping Step 2.5 leads to preflight failure loops that the operator cannot escape without editing Bicep. See "Fresh-Sub Automation Gaps" section at end of this file for the full evidence base (customer-provisioning-orchestration-r1 lessons learned 2026-08-22).

Step 2 performs **client-side validation only** (no L2 POST). It:
- Re-validates intake JSON against `intake.schema.json` (idempotent with Step 1.0 batch validate; belt-and-suspenders for interactive mode)
- Runs Step 0.5 iteration once more if any prereqs are scoped `once_per_customer_pre_intake` (per EXEC-10 scoping — none in current manifest, but reserved for future extensibility)
- Performs the SPAARKE customer-run history probe (Step 1a semantics — Dataverse MCP alt-key GET on `sprk_dataverseenvironment` filtered by `sprk_customerid`) to detect upgrade-mode
- Builds the run plan (handler list per profile + estimated cost + estimated duration)
- Displays the plan to the operator

The plan is presented; NOTHING mutates on L2 or in Azure. The operator sees the full picture BEFORE the confirmation gate fires.

```
PREFLIGHT (client-side) RESULT
  Duration: 3.2s
  [PASS] Intake JSON valid (14 fields present, all required)
  [PASS] Step 0.5 pre-intake prereqs — 26 of 26 checked
  [PASS] Customer history — new customerId (fresh provision, not upgrade)
  [PLAN] Handlers to execute (Model1Shared: ~19 handlers)
  [PLAN] Estimated duration: 42 min (H1-H14 sequential critical path)
  [PLAN] Estimated cost impact: +$412/mo (Model 1 marginal, within $430 envelope)
  [PLAN] Manual gates likely: none for Model 1 Shared
```

**Note**: server-side preflight (H0 handler) will run automatically when Step 4 POSTs `/api/runs`; H0 is the FIRST handler in the L2 DAG per `DagAdvancer.cs`. There is no separate "preflight-only" run mode — that concept was a skill fiction. If the operator wants H0-only re-verification WITHOUT triggering H1+, the actual mechanism is `POST /api/runs/{runId}/preflight?customerId={cid}` per `RunsEndpoints.cs:188` on an EXISTING run (upgrade-mode use case).

**Cost-envelope pre-check (BAT-10, SESSION 16)** — Step 2 computes an estimated cost impact locally (from tier + tenancyModel + region). H0's server-side check is the AUTHORITY; the client-side estimate here is a fast fail-close BEFORE Step 4 POST when the intake obviously exceeds the tier ceiling. `$script:BatchCostEnvelopePolicy` (bound at Step 1.0) drives the branch:

```powershell
# Client-side envelope check (rough — H0 is the authority)
$tierCap = switch ($tier) { 'shared-trial' { 430 } 'smb' { 700 } 'enterprise' { 2500 } 'dedicated' { 5000 } default { $null } }
if ($tierCap -and $estimatedMonthlyUsd -gt $tierCap) {
  if ($script:SkipInteractiveIntake) {
    switch ($script:BatchCostEnvelopePolicy) {
      'abortOnOverrun' {
        $diag = @{ runId='pre-dispatch'; customerId=$customerId; estimated=$estimatedMonthlyUsd; cap=$tierCap; policy='abortOnOverrun' } | ConvertTo-Json
        Set-Content -Path "runs/pre-dispatch-cost-overrun.json" -Value $diag
        Write-Error "[skill] Batch HARD STOP (BAT-10, costEnvelopePolicy=abortOnOverrun): estimated `$$estimatedMonthlyUsd/mo exceeds tier '$tier' cap `$$tierCap/mo. Diagnostic: runs/pre-dispatch-cost-overrun.json"
        exit 1
      }
      'warnAndProceed' {
        # Already rejected for Model2Dedicated at Step 1.0 — reaching here means Model1Shared shared-trial
        Write-Warning "[skill] Batch cost overrun ACKNOWLEDGED (BAT-10, warnAndProceed, Model 1 shared-trial only): estimated `$$estimatedMonthlyUsd/mo exceeds tier '$tier' cap `$$tierCap/mo. Proceeding per intake policy."
        $script:CostWarningLogged = $true
        Set-Content -Path "runs/pre-dispatch-cost-warning.json" -Value (@{estimated=$estimatedMonthlyUsd; cap=$tierCap; acknowledged='intake.costEnvelopePolicy=warnAndProceed'} | ConvertTo-Json)
      }
    }
  } else {
    Write-Warning "❌ Estimated `$$estimatedMonthlyUsd/mo exceeds tier '$tier' cap `$$tierCap/mo."
    $answer = Read-Host "Proceed anyway? (yes/no)"
    if ($answer -ne 'yes') { Write-Error 'Aborted at Step 2 cost envelope prompt.'; exit 1 }
  }
}
```

If Step 2 client-side validation FAILS, present the failure + escalation instructions. Do NOT proceed to Step 3.

---

### Step 2.5: Fresh-Sub Deployment Feasibility Check (NEW — customer-provisioning-orchestration-r1 lessons 2026-08-22)

**When to run**: Target Azure subscription was created within the last 90 days, OR this is the FIRST Bicep deploy attempt against this subscription in this region. Fresh subs have gotchas Microsoft has quietly introduced since 2024-2025 that break naive "just deploy" flows. These checks run OPERATOR-SIDE (in this skill) before invoking L2 H0, because L2 doesn't have the visibility (or the mandate) to modify region defaults or Bicep params.

Full evidence base: `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` (findings F1-F9).

Automated checks (each MUST pass or auto-remediate before Step 2):

**F1 — OpenAI model pin freshness**:
```powershell
$pins = (Get-Content infrastructure/bicep/stacks/{stack}.bicep | Select-String "version: '(\d{4}-\d{2}-\d{2})'").Matches.Groups[1].Value
foreach ($pin in $pins) {
  $models = az cognitiveservices model list --location $openAiLocation --query "[?kind=='OpenAI' && model.version=='$pin']" -o json | ConvertFrom-Json
  if ($models[0].model.lifecycleStatus -in @('Deprecating','Deprecated')) {
    # HALT — pin is dead. Recommend bump to latest GA/Legacy in same family.
  }
}
```

**F2 — Deployment SKU compatibility**:
- Verify each pinned model's `sku` field is in the list returned by `az cognitiveservices model list --query "[?...].model.skus"`
- gpt-5.x family REQUIRES `GlobalStandard` (gpt-5-pro literally supports NO other SKU)
- Legacy gpt-4o family accepts `Standard`

**F3 — Primary region App Service quota feasibility**:
```powershell
# Deploy a throwaway S1 App Service Plan via what-if to test quota
az deployment group what-if --resource-group $tempRg --template-file .claude/skills/provision-environment/refs/test-s1-plan.bicep `
  --parameters location=$candidateRegion --subscription $subId
# If SubscriptionIsOverQuotaForSku → this region has quota walls; auto-fallback to westus2 (Spaarke canonical)
```

**F4 — OpenAI region GA availability**:
```powershell
# Confirm all pinned models are GA in the intended sharedOpenAiLocation
az cognitiveservices model list --location $sharedOpenAiLocation --query "[?kind=='OpenAI' && model.name=='$pinnedModelName']" -o table
# Empty result → model not offered in this region; pivot sharedOpenAiLocation (canonical: westus3 when primary is westus2)
```

**F5 — Auto-allocated TPM detection**:
```powershell
az rest --method get --url "https://management.azure.com/subscriptions/$subId/providers/Microsoft.CognitiveServices/locations/$openAiRegion/usages?api-version=2023-05-01" `
  --query "value[?limit != '0']" -o json
```
- Enumerate what's already granted
- If pinned deployment set exceeds auto-granted TPM AND no auto-file-support-ticket flow available → auto-recompose deployment set to use ONLY auto-granted resources (documented downgrade with operator notification)

**F6 — Provider registration retry-verify loop**:
```powershell
foreach ($ns in $requiredProviders) {
  az provider register --namespace $ns --subscription $subId -o none
  $deadline = (Get-Date).AddMinutes(5)
  while ((Get-Date) -lt $deadline) {
    $state = az provider show -n $ns --query registrationState -o tsv
    if ($state -eq 'Registered') { break }
    Start-Sleep 30
  }
  if ($state -ne 'Registered') {
    # HALT — provide operator with Portal link: https://portal.azure.com/#view/HubsExtension/BrowseAll → subscription → Resource providers → search "$ns" → Register
  }
}
```

**F7 — Fresh-sub UX preamble**:
- Warn operator: "Portal Usage+Quotas dropdown will show empty until resources exist; use https://ai.azure.com Quotas for OpenAI TPM visibility"

**F8 — Auto-file support ticket** (advanced, requires `Microsoft.Support/*` permissions on the sub):
- If NO auto-grant path exists for a required resource AND operator has Support Plan → auto-file via `az support in-subscription tickets create`
- If no Support Plan → HALT with actionable operator guidance

**F10 — Global resource-name availability pre-check** (added 2026-08-22 after F10 discovery):
```powershell
# Service Bus namespace (Azure reserves suffixes like -sb globally)
az rest --method post --url "https://management.azure.com/subscriptions/$subId/providers/Microsoft.ServiceBus/checkNameAvailability?api-version=2022-10-01-preview" `
  --body "{`"name`":`"$sbName`",`"type`":`"Microsoft.ServiceBus/namespaces`"}"
# Storage account (global namespace)
az storage account check-name --name $storageName
# Cognitive Services custom subdomain (global namespace)
az cognitiveservices account check-domain-availability --subdomain-name $openAiName --type OpenAI
```
what-if does NOT run these checks — only actual create-time validation catches global-namespace conflicts. This gap wasted 16m35s on this session's first deploy attempt. Skill Step 2.5 MUST run these before invoking `az deployment sub create`.

**F9 — Support Plan check**:
```powershell
$plan = az rest --method get --url "https://management.azure.com/subscriptions/$subId/providers/Microsoft.Resources/checkResourceName?api-version=2020-10-01" 2>&1
# Check if sub has Support Plan attached; downgrade approach if not (never queue ticket-dependent action)
```

**Auto-remediation vs HALT decision matrix**:
| Finding | Auto-remediate? | Fallback |
|---|---|---|
| F1 (pin stale) | NO (requires operator ADR-020 sign-off on new pin) | HALT + recommend bump |
| F2 (SKU wrong) | YES (bicepparam auto-generation) | Log the change |
| F3 (region quota wall) | YES (fallback to westus2) | Log region pivot with rationale |
| F4 (OpenAI region absence) | YES (fallback sharedOpenAiLocation to westus3) | Log the split |
| F5 (auto-quota mismatch) | YES (recompose deployment set to auto-granted subset) | Notify operator: MVP downgrade with upgrade path |
| F6 (provider reg hang) | Retry 5 min, then HALT | Portal link |
| F7 (UX preamble) | Informational — always show | N/A |
| F8 (support ticket needed) | YES if Support Plan available | HALT if not |
| F9 (no support plan) | Downgrade to no-ticket-dependent approach | N/A |

**Skill output on completion of Step 2.5**:
```
FRESH-SUB FEASIBILITY (customer-provisioning-orchestration-r1 lessons):
  [PASS/AUTO-FIX/HALT] F1 OpenAI pin freshness: 3 of 3 pins GA in westus3
  [AUTO-FIX] F3 Primary region: eastus quota wall detected → auto-pivoted to westus2
  [AUTO-FIX] F4 OpenAI region: gpt-5 absent in westus2 → sharedOpenAiLocation=westus3
  [AUTO-FIX] F5 Auto-quota: gpt-5.4 GlobalStandard=0 TPM → recomposed to gpt-5-mini (500 TPM auto-granted)
  [PASS] F6 All required providers registered
  [PASS] F9 Support Plan available (Basic) — support-ticket path enabled if needed

Proceeding to Step 2 (L2 H0 preflight)...
```

**Current status of Step 2.5 automation** (as of 2026-08-22):
- MVP: Step 2.5 exists as INFORMATIONAL — operator is prompted to check findings F1-F9 manually with the queries above
- Full E2E-no-human-interaction: Step 2.5 becomes FULLY AUTOMATED — the skill absorbs each check as an automation, with the auto-remediation matrix above driving behavior
- Owner directive 2026-08-22: "the expectation for the final delivered solutions is that this process will run E2E with no human interaction. Ultimately the best solution is we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything." Absorbing F1-F9 into Step 2.5 automation is the largest single gap between MVP and full E2E.

---

### Step 3: Confirmation Gate — explicit "proceed" phrase required

**This is the critical decision gate. Nothing mutates the target env until the operator says the exact phrase.**

Present the full run plan:

```
RUN PLAN

  customerId:    trial-acme-2026-08-18
  tenantId:      12345678-...
  tenancyModel:  Model1Shared
  profile:       dev

  Handlers to execute (Model 1 Shared: 19 / Model 2 Dedicated: 19 — 20 total, minus 1 per tenancy model):
    H0        preflight (unconditional; first handler in DAG per DagAdvancer.cs)
    H0.5      consent-callback (Model 2 only — skipping for Model 1)
    H1        resource-group provisioning
    H2a       Bicep infra apply (30-min timeout)
    H2b       AI Search index deploy (7 canonical indexes)
    H3        KV secret bootstrap
    H4        canonical secret population (per-tenant KV; literal values)
    H4-shared canonical secret population from source Azure services (shared KV; extract-from-source recipes — F19; task 200)
    H4b       bulk App Service app-settings from canonical manifest (~80-160 settings in ONE batch → ONE restart; F20/F20a; task 201)
    H5        Dataverse environment creation (20-min timeout for Model 2)
    H6        Dataverse solutions import (8 solutions, dependency-ordered)
    H7        env-var writes to customer env
    H8        SPE container-type creation (empirically near-instant, 25h fallback ceiling; H8.a re-verifies)
    H9        BFF deploy to customer stamp (blue-green via staging slot; runs AFTER H4-shared + H4b so BFF boots with config in place — HANDLER-01 DAG fix SESSION 15)
    H10       Dataverse App User creation (UAMI-based)
    H11       demo user provisioning (Model 1 only — trial users; skipping for Model 2)
    H12a      AI seed chain (playbooks + embeddings)
    H12b      playbook consumers seed
    H12c      agents seed
    H13       acceptance gate (all traps clear + invariants pass + cost envelope)
    H14       Exchange ApplicationAccessPolicy verification (T4)

  Estimated wall-clock: 42 min (no lead-time gates surfaced by H0)
  Estimated cost impact: +$412/mo (Model 1 marginal, within envelope)

  Manual gates you MAY encounter mid-run:
    - Model 2 admin consent URL (H0.5) — customer admin clicks
    - Azure quota bump (if H1 hits soft cap) — operator opens support ticket; advance via /gates/{gateId}/advance (SKILL-07 fix)
    - SPE container replication wait (H8) — empirically near-instant per operator memory feedback_spe_container_timing; 25h fallback ceiling in the SKILL, rarely fires

  DESTRUCTIVE OPERATIONS: none in fresh-provisioning mode. Upgrade mode may
  overwrite Bicep-managed resources with drift; if this is an upgrade run,
  H2a's what-if diff will be presented separately before applying.

To proceed with this run, type the exact phrase:

    proceed with provisioning

(a bare "y" or "yes" is NOT accepted at this gate — spec §4.3a.4)
```

Wait for the literal string `proceed with provisioning`. Anything else — including "y", "yes", "go", "ok" — prompts a re-ask with the same gate. This is by design per NFR-11 auditability (the operator's explicit phrase is captured in the run's audit trail).

**Batch mode (BAT-03, SESSION 16)** — the Step 3 gate is enforced at Step 1.0 (JSON schema `const:"proceed with provisioning"` on `intake.confirmationAcknowledgment`, PLUS the Step 1.0 branch that explicitly matches the string and exits on drift). Step 3 does NOT re-prompt in batch mode; the intake-level attestation is what carries the auditable intent. The SHA-256 of the intake file is captured into `nonSecretParameters.intakeFileSha256` at Step 4.0 so the L2 audit record can prove-back which exact intake JSON authorized this run:

```powershell
if (-not $script:SkipInteractiveIntake) {
  # INTERACTIVE — retry-until-literal Read-Host loop
  do {
    $phrase = Read-Host
    if ($phrase -ne 'proceed with provisioning') {
      Write-Host "(literal phrase required; try again OR press Ctrl+C to abort)"
    }
  } until ($phrase -eq 'proceed with provisioning')
  $confirmationPhrase = $phrase
} else {
  # BATCH — attestation already validated at Step 1.0; carry the phrase into Step 4.0
  $confirmationPhrase = 'proceed with provisioning'
  Write-Host "  [BATCH] Confirmation gate satisfied by intake.confirmationAcknowledgment (validated at Step 1.0)." -ForegroundColor Cyan
}
```

---

### Step 4: Execute — issue THE single POST + poll → advance

Once Step 3's `proceed with provisioning` phrase captured (interactive) OR `confirmationAcknowledgment` field validated (batch), Step 4 issues THE single POST that mutates L2 state. L2 unconditionally enqueues H0 → reconciler dispatches H1 → H2a → ... → H13 → H14 without further operator input.

#### 4.0. Enqueue

Per Wave 0 Decision 1 (`tenantId` flows via `nonSecretParameters`) + Decision 6 (mechanical prune to match `CreateRunRequest` top-level shape) + Decision 3 (`confirmationAcknowledgment` in nonSecretParameters). `CreateRunRequest` (`RunsEndpoints.cs:861-880`) accepts EXACTLY `customerId, environmentId, tenancyModel, profile, nonSecretParameters` — no `tenantId` top-level, no `mode`.

```powershell
$intakeFileSha256 = if ($BatchIntakeFile) { (Get-FileHash -Path $BatchIntakeFile -Algorithm SHA256).Hash } else { $null }

# --- ISH-02 subscriptionId flow (Wave 0 Decision 6 + Step-2-body-construction, SESSION 16) ---
# Model2Dedicated: intake.subscriptionId is REQUIRED (per intake.schema.json allOf constraint).
# Model1Shared: intake.subscriptionId is OPTIONAL; when omitted the skill auto-defaults to the
# Spaarke shared subscription for the target env (looked up from spaarke-constants.yaml or
# az account context — env-specific).
if ($tenancyModel -eq 'Model2Dedicated') {
  if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
    Write-Error "[skill] Step 4.0 HARD STOP: Model2Dedicated run requires intake.subscriptionId (customer's own subscription per ADR-027 D4). Missing at dispatch → H1 fail-fast within ~20s with MissingSubscriptionId. Correct the intake and rerun."
    exit 1
  }
  $resolvedSubscriptionId = $subscriptionId
} else {
  # Model1Shared: auto-default from az context if not supplied
  $resolvedSubscriptionId = if ($subscriptionId) { $subscriptionId } else { az account show --query id -o tsv }
  if ([string]::IsNullOrWhiteSpace($resolvedSubscriptionId)) {
    Write-Error "[skill] Step 4.0 HARD STOP: Model1Shared run — no subscriptionId in intake and az account show returned empty. Run `az login` and retry."
    exit 1
  }
}

# --- openAiRegion → openAiLocation mapping (Bicep param name is openAiLocation, intake field is openAiRegion) ---
$resolvedOpenAiLocation = if ($openAiRegion) { $openAiRegion } else { 'westus3' }  # canonical Spaarke default per operator memory reference_azure_fresh_sub_regional_gotchas

$body = @{
  customerId    = $customerId
  environmentId = $environmentId          # created at Step 1f
  tenancyModel  = $tenancyModel           # Model1Shared | Model2Dedicated
  profile       = $profile                # one of 3 enum values per Step 1e
  nonSecretParameters = @{
    tenantId                    = $tenantId              # I1 invariant per Wave 0 Decision 1
    subscriptionId              = $resolvedSubscriptionId # ISH-02 — consumed by H1/H2a/H2b/H4/H4b/H4Shared/H8/H9/H13/H14
    openAiLocation              = $resolvedOpenAiLocation # Bicep param name (openAiLocation), NOT openAiRegion; intake field renamed at the boundary
    confirmationAcknowledgment  = $confirmationPhrase     # verbatim "proceed with provisioning"
    intakeFileSha256            = $intakeFileSha256       # batch-mode audit trail (null in interactive)
    region                      = $region                 # primary platform region (e.g. westus2) — distinct from openAiLocation
    tier                        = $tier                   # COMP-10 gate input (H0Options.GetCeilingUsd lookup key)
    estimatedMonthlyUsd         = $estimatedMonthlyUsd    # COMP-10 gate input (Bucket A HIGH#8 SESSION 18); null → H0 log-only skips
    costEnvelopePolicy          = $script:BatchCostEnvelopePolicy  # COMP-10 gate policy (Bucket A HIGH#8 SESSION 18); default 'abortOnOverrun' in batch loader. Interactive mode leaves $script:BatchCostEnvelopePolicy null → H0 treats null as abortOnOverrun-equivalent per its default branch.
    operatorUpn                 = $operatorUpn
    # other operator-supplied intake fields (notes, etc.) can be added here; the L2 side
    # treats nonSecretParameters as a bag and ignores unknown keys (§4D-adjacent design).
    # DO NOT include the SKILL-LOCAL batch policy fields (mcpDisconnectPolicy / acknowledgeUpgradeMode /
    # onFailedPolicy / onQuarantinedPolicy / onManualGatePolicy / postmortemFile) — those are
    # BAT-01..09 control-flow knobs, NOT L2 payload. They control this skill's control flow at
    # Steps 0d/1a/1g/4b/5/7b and would be noise on the L2 audit record.
    # NOTE (Bucket A HIGH#8 SESSION 18): costEnvelopePolicy is deliberately IN the payload — the
    # server-side H0 cost-envelope gate needs it to branch abort-vs-warnAndProceed. Prior guidance
    # to exclude it left COMP-10 fully un-wired end-to-end (H0 always hit the disabled/skip branch).
  }
} | ConvertTo-Json -Depth 5

$response = Invoke-RestMethod `
  -Uri "$l2Base/api/runs" `
  -Method POST `
  -Headers @{ Authorization = "Bearer $token" } `
  -Body $body -ContentType "application/json"

$runId = $response.runId  # response shape: { runId, customerId, status:"NotStarted", location:"/api/runs/{runId}?customerId=..." }
```

L2 returns 202 within 100ms and the reconciler picks up H0 within ~5s. L2's state reconciler then auto-advances the DAG — `POST /api/runs/{id}/resume` is ONLY for retry of a `Failed` run per `RunsEndpoints.cs:232-244`, NOT a transition trigger.

#### 4a. Poll loop

Poll `GET /api/runs/{runId}?customerId={customerId}` at **10s intervals**. The `?customerId=` query parameter is MANDATORY per `RunsEndpoints.cs:582` (`TryValidateRouteAndPartition` returns 400 if missing).

```powershell
# URL-encode customerId per RFC 3986; assume already-safe kebab-case per Step 1a validation but escape defensively.
# Bucket B LOW#4 SESSION 18 (customer-provisioning-orchestration-r1 adversarial e2e verify workflow wepdcb8we):
# implement token auto-refresh on 401 with a bounded retry counter — Prior version was prose-only.
# A Model 2 run entering H0.5 waiting on customer admin consent + 45min operator idle would blow past the
# ~1h L2-audience token TTL; the naive Invoke-RestMethod call would throw HttpResponseException 401 and
# either crash the skill (no handoff) or hit an unbounded retry loop with the expired token.
$encodedCustomerId = [Uri]::EscapeDataString($customerId)
$maxConsecutive401 = 3   # after this many, escalate per Fallback F2 (line 1809)
$consecutive401 = 0

function Invoke-L2PollWithTokenRefresh {
    param([string]$Uri, [string]$Env)
    $script:consecutive401 = 0
    while ($true) {
        try {
            return Invoke-RestMethod -Uri $Uri -Method GET -Headers @{ Authorization = "Bearer $script:token" }
        }
        catch [System.Net.Http.HttpRequestException] {
            # PowerShell 7+: HttpRequestException.StatusCode is HttpStatusCode?.
            $statusCode = $_.Exception.StatusCode
            if ($statusCode -ne 'Unauthorized') { throw }
            $script:consecutive401++
            if ($script:consecutive401 -ge $maxConsecutive401) {
                throw "Poll loop hit $maxConsecutive401 consecutive 401s after token refresh — escalate per Fallback F2 (line 1809). Operator's AAD context is broken; run 'az login' + rerun skill with -Resume $runId."
            }
            Write-Warning "Poll got 401 (attempt $script:consecutive401/$maxConsecutive401) — re-acquiring L2-audience token via 'az account get-access-token' then retrying ONCE."
            $script:token = az account get-access-token --resource "api://spaarke.com/provisioning-controlplane-$Env" --query accessToken -o tsv 2>$null
            if ([string]::IsNullOrWhiteSpace($script:token)) {
                throw "az account get-access-token returned empty for api://spaarke.com/provisioning-controlplane-$Env — az context lost. Run 'az login' + rerun skill with -Resume $runId."
            }
            # Loop continues → retry with fresh token.
        }
    }
}

$run = Invoke-L2PollWithTokenRefresh -Uri "$l2Base/api/runs/$runId`?customerId=$encodedCustomerId" -Env $environment
```

**Reconciler liveness check (EXEC-05)**: if 3 consecutive polls return identical `updatedAt` on the run doc AND the current handler is not one of the long-running ones (H2a bicep = 30min, H8 SPE 25h fallback, H12a AI-seed = 15min), fetch `$l2Base/healthz` to verify L2 is still up, then issue `POST /api/runs/{runId}/resume?customerId={cid}` (which per RunsEndpoints.cs re-enqueues the CurrentPhase envelope). This nudges a stuck reconciler; do NOT auto-retry if it doesn't unstick within another 3 polls — escalate as Fallback F3.

**Bucket B MED#8 SESSION 18 clarification for EXEC-05 semantics** (customer-provisioning-orchestration-r1 adversarial e2e verify workflow wepdcb8we): Step 4b at line 1140/1337 documents `/resume` as "ONLY for retrying a Failed run per RunsEndpoints.cs:232-244". EXEC-05's use of `/resume` against a `Running` run is a documented DIVERGENCE from that contract, permitted because: (a) `RunsEndpoints.cs` PostResume does NOT gate on Status — it re-enqueues the CurrentPhase envelope regardless (verified against Reconciler liveness precedent, task 107); (b) the L2 dispatcher's Level-1 Service Bus dedup + Level-3 handler CompletedPhase check ensure the re-enqueued envelope is a no-op if the handler has already completed the current phase; (c) the retry counter mutation in HandlerOutcomeApplier (task 107) only fires on Failure branches, NOT on Running-branch re-dispatches, so EXEC-05 does NOT decrement any retry budget. If a future L2 change adds a status-gate to PostResume (e.g., rejecting non-Failed runs with 409), EXEC-05 breaks and this note must be removed. Verified 2026-08-27 against RunsEndpoints.cs:232-244 (no status gate present).

Track TodoWrite entries for each handler as it enters/exits `Running`:
- `Handler H2a (bicep-apply) — Running`
- `Handler H2a (bicep-apply) — Completed (28m 14s)`
- `Handler H6 (dv-solutions) — Running`
- ...

Present progress to the operator every ~5 completed handlers OR on any state transition (`WaitingOnGate`, `Failed`, `Quarantined`, `Cancelled`).

#### 4b. Handle each terminal state (RunStatus enum per `ProvisioningRun.cs:212-239`)

The run's `status` field transitions through the actual enum values — NOT the fictional Accepted/Executing/Succeeded/Drifted values earlier drafts of this skill listed:

| Status | Meaning | Skill action |
|---|---|---|
| `NotStarted` | POST /api/runs returned 202; H0 not yet dequeued from Service Bus | Poll |
| `Running` | Handlers actively executing per the reconciler DAG | Poll; update TodoWrite; apply EXEC-05 liveness nudge if stuck |
| `WaitingOnGate` | Handler paused pending external condition (H0.5 admin consent, H1 quota, H8 SPE replication) | See Step 5 (manual gate handling) |
| `Completed` | All handlers completed + H13 acceptance passed | See Step 6 (completion handoff) |
| `Failed` | Handler failed with `Retryable*` or `Resumable` class per §4C rollback taxonomy | Present failure + `POST /api/runs/{id}/resume?customerId=` option to operator |
| `Cancelled` | Operator called `POST /api/runs/{id}/cancel`; sprk_currentrunid released (EXEC-07 fix) | Report cancellation; no auto-restart |
| `Quarantined` | Handler failed with `QuarantineRequired` class | HARD STOP; require `POST /api/runs/{id}/clear-quarantine?customerId=` with reason + audit trail |

There is NO `Drifted` state — drift is detected inline by H13 and surfaces as `Failed` with a specific rejection code (upgrade-drift-detected).

Do NOT auto-retry `Failed` runs. Auto-retry hides operator-actionable diagnostics. Ask.

**Batch-mode terminal-state handling (BAT-07, SESSION 16)**:

```powershell
switch ($run.status) {
  'Completed' {
    # Interactive AND batch: proceed to Step 5 (manual gate handling) / Step 6 (completion handoff)
  }
  'Failed' {
    if ($script:SkipInteractiveIntake) {
      # BATCH — $script:BatchOnFailedPolicy is 'autoResumeOnce' | 'abandon' (default)
      switch ($script:BatchOnFailedPolicy) {
        'autoResumeOnce' {
          if (-not $script:AlreadyResumed) {
            Write-Host "  [BATCH] onFailedPolicy=autoResumeOnce → POST /api/runs/$runId/resume?customerId=$encodedCustomerId (single attempt)" -ForegroundColor Yellow
            Invoke-RestMethod -Uri "$l2Base/api/runs/$runId/resume`?customerId=$encodedCustomerId" -Method POST -Headers @{ Authorization = "Bearer $token" }
            $script:AlreadyResumed = $true
            continue  # back to poll loop
          }
          # Fall through — already resumed once and still Failed. Write diag + exit.
        }
        'abandon' { <#  fall through — write diag + exit #> }
      }
      $diag = @{
        runId       = $runId
        customerId  = $customerId
        finalStatus = 'Failed'
        policy      = $script:BatchOnFailedPolicy
        currentPhase = $run.currentPhase
        rejection   = $run.rejectionCode
        message     = $run.rejectionMessage
      } | ConvertTo-Json -Depth 4
      $diagPath = "runs/$runId-failed.json"
      New-Item -Path (Split-Path $diagPath) -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null
      Set-Content -Path $diagPath -Value $diag
      Write-Error "[skill] Batch HARD STOP (BAT-07, onFailedPolicy=$($script:BatchOnFailedPolicy)): run $runId ended Failed at phase $($run.currentPhase). Diagnostic: $diagPath"
      # Still writes lessons-learned.md via Step 7 (postmortem is UNCONDITIONAL)
      # Non-zero exit is 2 per BAT-07 convention.
      exit 2
    }
    # INTERACTIVE — ask operator
    Write-Host "❌ Run FAILED at phase $($run.currentPhase). Rejection: $($run.rejectionMessage)"
    $answer = Read-Host "Resume this run? (yes/no)"
    if ($answer -eq 'yes') { Invoke-RestMethod -Uri "$l2Base/api/runs/$runId/resume`?customerId=$encodedCustomerId" -Method POST -Headers @{ Authorization = "Bearer $token" }; continue }
    Write-Error "Run abandoned by operator."
    exit 2
  }
  'Quarantined' {
    # $script:BatchOnQuarantinedPolicy is 'failFast' (enum-of-one for forward compat)
    $diag = @{
      runId       = $runId
      customerId  = $customerId
      finalStatus = 'Quarantined'
      quarantineReason = $run.quarantineReason
      remedy      = "Manually invoke QuarantineClearService via a separate maintenance workflow — batch mode never auto-clears quarantine per BAT-07 rationale (unattended runs must not silently clear operator-required state)."
    } | ConvertTo-Json -Depth 4
    $diagPath = "runs/$runId-quarantine.json"
    New-Item -Path (Split-Path $diagPath) -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null
    Set-Content -Path $diagPath -Value $diag
    Write-Error "[skill] HARD STOP (BAT-07, Quarantined): run $runId requires manual QuarantineClearService intervention. Diagnostic: $diagPath. Postmortem will still be written via Step 7."
    exit 3
  }
  'Cancelled' {
    Write-Warning "Run cancelled. sprk_currentrunid released."
    exit 5
  }
  default {
    # NotStarted / Running / WaitingOnGate — keep polling; WaitingOnGate handled in Step 5
    continue
  }
}
```

---

### Step 5: Manual Gate Handling

Some handlers reach `WaitingOnGate` because they require operator-visible action:

**Batch-mode dispatch (BAT-08, SESSION 16)** — the interactive sub-flows below (5a/5b/5c/5d) assume a live operator at stdin. In batch mode (`$script:SkipInteractiveIntake -eq $true`), the shared dispatch block below runs FIRST and short-circuits the interactive sub-flows per `$script:BatchOnManualGatePolicy`:

```powershell
if ($script:SkipInteractiveIntake -and $run.status -eq 'WaitingOnGate') {
  $gateInfo = @{
    runId       = $runId
    customerId  = $customerId
    gateId      = $run.gateId
    handler     = $run.currentPhase
    reason      = $run.gateReason
    instructions = $run.gateInstructions
    detected    = (Get-Date -Format 'o')
  } | ConvertTo-Json -Depth 4
  $gatePath = "runs/$runId-gate.json"
  New-Item -Path (Split-Path $gatePath) -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null
  Set-Content -Path $gatePath -Value $gateInfo

  switch ($script:BatchOnManualGatePolicy) {
    'waitAndExit' {
      # Write WAITING marker so operator resume flow can pick up
      $waitingMd = @"
# Run $runId — WAITING at manual gate

- **Gate**: $($run.gateId) (handler: $($run.currentPhase))
- **Reason**: $($run.gateReason)
- **Instructions**: $($run.gateInstructions)
- **Detected**: $(Get-Date -Format 'o')

## Resume
After clearing the gate condition, rerun the skill with:
    /provision-environment $customerId --batch <original-intake.json> --resume $runId
"@
      $waitingPath = "runs/$runId-WAITING.md"
      Set-Content -Path $waitingPath -Value $waitingMd
      Write-Warning "[skill] Batch WAITING (BAT-08, onManualGatePolicy=waitAndExit): run $runId hit gate '$($run.gateId)'. Wrote $waitingPath + $gatePath. Exit 4."
      exit 4
    }
    'pollUntilTimeout' {
      # Bucket B MED#13 SESSION 18 (customer-provisioning-orchestration-r1
      # adversarial e2e verify workflow wepdcb8we): branch the poll cap on
      # gate identity. Step 5c prose promises H8 SPE container-type replication
      # a 25h fallback (per MS's documented 24h SLO), but the default 30-min
      # cap would prematurely exit-4 on a genuinely-slow replication event.
      # Other gates (H0.5 admin consent, H1 quota bump) legitimately deserve
      # the 30-min ceiling — operator escalates to Fallback F3 after that.
      # Empirical practice per operator memory feedback_spe_container_timing:
      # SPE replication is near-instant (~2 min in 2026-08-22 Model 1 stand-up),
      # so the 25h ceiling is defensive and almost never fires in real dispatches.
      $isSpeReplication = ($run.gateId -eq 'spe-replication') -or
                          ($run.currentHandler -eq 'H8')
      $capMinutes = if ($isSpeReplication) { 1500 } else { 30 }  # 1500 min = 25h for SPE, 30 min otherwise
      $capLabel = if ($isSpeReplication) { '25h SPE-replication fallback' } else { '30 min' }
      $deadline = (Get-Date).AddMinutes($capMinutes)
      Write-Host "[skill] Batch onManualGatePolicy=pollUntilTimeout — polling gate '$($run.gateId)' for clear ($capLabel hard cap)..." -ForegroundColor Cyan
      while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 10
        $run = Invoke-RestMethod -Uri "$l2Base/api/runs/$runId`?customerId=$encodedCustomerId" -Method GET -Headers @{ Authorization = "Bearer $token" }
        if ($run.status -ne 'WaitingOnGate') { break }
      }
      if ($run.status -eq 'WaitingOnGate') {
        Write-Warning "[skill] Batch WAITING (BAT-08, pollUntilTimeout hit $capLabel cap): run $runId still at gate '$($run.gateId)'. Exit 4."
        exit 4
      }
      # else fall through — status advanced; continue poll loop
      continue
    }
    'failFast' {
      Write-Warning "[skill] Batch WAITING (BAT-08, onManualGatePolicy=failFast): run $runId hit gate '$($run.gateId)' — exiting immediately without poll. Exit 4."
      exit 4
    }
  }
}
```

Interactive-mode sub-flows below assume a live operator; batch mode returns before reaching them.

#### 5a. H0.5 Model 2 admin consent (Model 2 only)

```
🔔 MANUAL GATE: Customer admin consent required (Model 2)

  Handler: H0.5 consent-callback
  Reason:  The multi-tenant BFF app-reg needs admin consent on the customer's
           Entra tenant before H5 can create a Dataverse Application User.

  ACTION FOR CUSTOMER ADMIN (send this URL to the customer — skill substitutes {tokens} before display):
    URL construction:
      $bffAppId = $constants.spaarke.bffMultiTenantAppId  # from spaarke-constants.yaml per PLX-13
      $callback = "$($constants.spaarke.bffProdBase)/api/onboarding/consent-callback"
      $consentUrl = "https://login.microsoftonline.com/$tenantId/adminconsent" +
                    "?client_id=$bffAppId&redirect_uri=$([Uri]::EscapeDataString($callback))&state=$runId"
      Write-Host $consentUrl

    Example (shape only — real values substituted at runtime):
      https://login.microsoftonline.com/<customer-tenant-guid>/adminconsent
        ?client_id=<multitenant-bff-app-id>
        &redirect_uri=https%3A%2F%2Fspaarke-bff-prod.azurewebsites.net%2Fapi%2Fonboarding%2Fconsent-callback
        &state=<runId>

  The customer admin clicks, signs in with a Global Admin account, and consents.
  H0.5 will auto-detect the callback (HMAC-verified) and advance the run.

  Skill will auto-poll every 30s for the next 2 hours. If no callback in 2h,
  the skill will pause + ask you what to do.

  Type 'status' anytime to see current wait state.
  Type 'abandon' to abort the run + quarantine (requires clear-quarantine to resume).
```

#### 5b. H1 Azure quota bump (rare)

```
🔔 MANUAL GATE: Azure quota bump required (H1)

  Handler: H1 resource-group provisioning
  Reason:  Target subscription hit soft cap on {vCPUs / P1v3 count / Storage GB /
           whatever the specific quota is}.

  ACTION FOR OPERATOR:
    1. Open Azure Portal → Subscription → Usage + Quotas → filter by {quota-name}
    2. Request quota increase (may require Microsoft support ticket)
    3. Wait for approval email (usually 15-60 min for standard bumps)
    4. Return here and type 'advance' to have L2 re-verify quota + release the gate

  Skill call on 'advance': POST /api/runs/{runId}/gates/{gateId}/advance?customerId={cid}
  (NOT /resume — /resume is for retrying a Failed run per RunsEndpoints.cs:232-244.)
```

#### 5c. H8 SPE container-type replication (per operator memory `feedback_spe_container_timing`, MS's documented 24h wait is near-instantaneous in practice)

```
🔔 MANUAL GATE: SPE container-type replication in progress (H8)

  Handler: H8 spe-container-create
  Reason:  Container-type created successfully; H8.a needs Microsoft-side replication
           to complete before it can verify or H9 can bind BFF to the container.

  ACTION: none required. The skill polls H8.a on this schedule:
    - Minutes 0-15: every 30-60s (empirical: near-instant per operator memory
      `feedback_spe_container_timing`; 2026-08-22 Model 1 Prod stand-up saw
      replication complete within ~2 min)
    - Minutes 15-60: every 5 min
    - Hour 1+: alert operator + fall back to 25h ceiling (defensive; almost
      never fires in practice)

  The skill will not exit the session; it stays on this gate until H8.a succeeds
  OR operator types 'abandon'.
```

#### 5d. Generic pattern for any other `WaitingOnGate`

```
🔔 MANUAL GATE: {gate name} ({handler name})

  Reason: {gate reason from L2 response}
  Action: {gate.instructions from L2 response}

  Type 'advance' when the action is complete — skill will POST
  /api/runs/{runId}/gates/{gateId}/advance?customerId={cid} and let L2 re-verify.
  Type 'abandon' to quarantine the run.
```

**IMPORTANT**: NEVER auto-advance past a gate by trusting the operator's assertion. Always call `POST /api/runs/{id}/gates/{gateId}/advance?customerId={cid}` (per `RunsEndpoints.cs` GateAdvance handler) and let L2 re-verify the underlying condition (Dataverse query, Graph query, Azure resource state). If verification fails, the run stays at `WaitingOnGate`. `/resume` is ONLY for retrying a Failed run — using it for gate advance either does nothing (if run is still WaitingOnGate) or wastes a retry budget entry (if run has since transitioned to Failed).

---

### Step 6: Completion Handoff

When the run reaches `Completed` (H13 acceptance passed — this is the terminal-success RunStatus per `ProvisioningRun.cs:212-239`; earlier drafts of this skill used the fictional `Succeeded`):

#### 6a. Update `sprk_dataverseenvironment` registry — TWO-STEP: read then update (HARD-STOP on any failure)

Per REG-04 (SESSION 15) — Step 6a is NOT belt-and-suspenders. The server-side updater at H13 writes ONLY `sprk_setupstatus` + `sprk_currentrunid` release (per `DataverseRegistrySetupStatusUpdater.cs`). The remaining Ready-state columns (`sprk_provisionedon`, `sprk_bffversion`, `sprk_solutionversion`, and per REG-01 also `sprk_azuresubscriptionid`, `sprk_resourcegroupname`, `sprk_appservicename`, `sprk_keyvaultname`, `sprk_containertypeid`, `sprk_ClientCacheBustToken`) are written by REG-01's H13 pre-Ready PATCH sub-step (SESSION 15 Wave 2 commit `328981ba2`) or, if that PATCH failed (leaving RunStatus=Running-blocked, not Completed), by the operator-side skill here. Either way, Step 6a re-verifies and, on drift, applies the missing PATCH from the operator's session.

Per Wave 0 Decision 2 (Dataverse MCP alt-key probe as the canonical registry lookup):

```powershell
# ---------------------------------------------------------------------------
# Bucket B HIGH#10 SESSION 18 (customer-provisioning-orchestration-r1
# adversarial e2e verify workflow wepdcb8we): Step 6a MUST NOT throw before
# Step 6b writes the handoff report. Per SKILL.md line 60 MUST rule, the
# handoff artifact `runs/{runId}.md` is a NON-NEGOTIABLE audit-trail obligation
# — it is written on EVERY terminal outcome (success + registry-clean, success +
# registry-stale, or hard failure). Prior behavior threw on the null-guards or
# on Invoke-RestMethod PATCH errors → Step 6b + 6c never ran → operator lost
# the mandatory audit artifact and had no diagnostic pointing at the failure.
#
# NEW STRUCTURE (Bucket B HIGH#10):
#   - Step 6a uses flags $script:RegistryStale + $script:RegistryStaleDiagnostic
#     to capture failure state INSTEAD OF throwing.
#   - Step 6b writes the handoff report UNCONDITIONALLY (adding a REGISTRY-STALE
#     section when the flag is set).
#   - Step 6c writes a separate `runs/{runId}-registry-stale.md` skeleton with
#     an actionable manual-recovery recipe (`pac data update` / Portal) when
#     the flag is set, then exits non-zero AFTER the handoff report is written.
#
# The single-writer invariant on sprk_currentrunid (Bucket B HIGH#7) is
# unaffected — this reshaping is purely about error-path ordering.
# ---------------------------------------------------------------------------
$script:RegistryStale = $false
$script:RegistryStaleDiagnostic = $null

# Step 1: lookup — resolve environmentId GUID. Prefer the value captured at Step 1f
# (skill session-local $environmentId). Fallback: query by sprk_customerid alt-key
# in case Step 1f state was lost across a compact/handoff.
if ([string]::IsNullOrWhiteSpace($environmentId)) {
  try {
    $lookup = mcp__dataverse__read_query(query = @"
      <fetch top="1">
        <entity name="sprk_dataverseenvironment">
          <attribute name="sprk_dataverseenvironmentid" />
          <filter><condition attribute="sprk_customerid" operator="eq" value="$customerId" /></filter>
        </entity>
      </fetch>
"@)
    $environmentId = $lookup.rows[0].sprk_dataverseenvironmentid
  } catch {
    $script:RegistryStale = $true
    $script:RegistryStaleDiagnostic = "environmentId lookup failed (MCP): $($_.Exception.Message)"
    Write-Warning "Step 6a environmentId lookup failed — will write registry-stale diagnostic AFTER handoff report. Diagnostic: $script:RegistryStaleDiagnostic"
  }
}
if (-not $script:RegistryStale -and -not ($environmentId -match '^[0-9a-fA-F-]{36}$')) {
  $script:RegistryStale = $true
  $script:RegistryStaleDiagnostic = "environmentId could not be resolved for customerId=$customerId — value='$environmentId' does not match GUID shape"
  Write-Warning "Step 6a HARD-WARN (Bucket B HIGH#10): $script:RegistryStaleDiagnostic. Handoff will still be written."
}

# Step 2: update — write the promoted columns (idempotent PATCH).
if (-not $script:RegistryStale) {
  try {
    mcp__dataverse__update_record(
      entityName = "sprk_dataverseenvironment",
      recordId   = $environmentId,
      fields = @{
        sprk_provisionedon            = $completedAtIso     # from run.CompletedOn
        sprk_bffversion               = $deployedBffVersion  # from run.InterStepState.BffVersion
        sprk_solutionversion          = $deployedSolutionVer # from run.InterStepState.SolutionVersion
        sprk_azuresubscriptionid      = $azureSubId
        sprk_resourcegroupname        = $rgName
        sprk_appservicename           = $appServiceName
        sprk_keyvaultname             = $kvName
        sprk_containertypeid          = $containerTypeId
        sprk_ClientCacheBustToken     = $cacheBustToken
        # sprk_setupstatus is set by the server (H13 updater).
        # sprk_currentrunid release is ALSO routed via ICustomerRunGuard.ReleaseAsync
        # per Bucket B HIGH#6/#7 SESSION 18 — do NOT clear it from this operator-side PATCH.
        # If drift detected on any set-once column, operator MUST HARD STOP + escalate — do NOT overwrite blindly.
      }
    )
  } catch {
    # F1 fallback path (Dataverse MCP disconnect) — use raw Web API PATCH with operator's az token.
    # Bucket A HIGH#13 SESSION 18 fix: previously read `$constants.spaarke[$environment].registryDvUrl`
    # which was a PHANTOM shape ($constants.spaarke.* never existed in spaarke-constants.yaml — the
    # real path is $constants.name_templates.registryDvUrl.{env}, matching Step 0.5b line 347).
    #
    # Bucket B HIGH#10 SESSION 18: nested try/catch here so a fallback failure ALSO writes to the
    # $script:RegistryStale flag instead of throwing. Step 6b runs unconditionally after this block.
    try {
      $dvUrl = $constants.name_templates.registryDvUrl.$environment  # e.g. https://spaarkedev1.crm.dynamics.com for dev per operator memory feedback_no_central_managing_env_yet
      if ([string]::IsNullOrWhiteSpace($dvUrl)) {
        throw "registry env dvUrl not resolvable for controlPlaneEnv='$environment' — verify scripts/provisioning-prereqs/spaarke-constants.yaml name_templates.registryDvUrl.$environment is populated"
      }
      $dvToken = az account get-access-token --resource $dvUrl --query accessToken -o tsv
      if ([string]::IsNullOrWhiteSpace($dvToken)) {
        throw "az token acquisition failed for resource '$dvUrl' — operator's AAD context lost between Step 0b and Step 6a (run 'az login')"
      }
      $body = @{ sprk_provisionedon = $completedAtIso; sprk_bffversion = $deployedBffVersion; ... } | ConvertTo-Json
      Invoke-RestMethod -Uri "$dvUrl/api/data/v9.2/sprk_dataverseenvironments($environmentId)" `
        -Method PATCH -Headers @{ Authorization = "Bearer $dvToken"; "OData-Version" = "4.0"; "If-Match" = "*" } `
        -Body $body -ContentType "application/json"
    } catch {
      $script:RegistryStale = $true
      $script:RegistryStaleDiagnostic = "BOTH Dataverse MCP AND raw Web API fallback failed (Bucket B HIGH#10). MCP: $($_.Exception.Message); Web API: $($_.Exception.Message). Manual recovery required via pac data update or Portal — see runs/{runId}-registry-stale.md."
      Write-Warning "Step 6a fallback failed — will write registry-stale diagnostic AFTER handoff report. Diagnostic: $script:RegistryStaleDiagnostic"
    }
  }
}
```

Note: `sprk_tenantid` MUST NOT be re-written here — it's set at placeholder-create (Step 1f) and NEVER changes for the customer's lifetime. Overwriting risks silent §4D I1 tenant-isolation invariant violation.

**Bucket B HIGH#10 (SESSION 18) reversal of the previous HARD STOP contract**: Step 6a failures no longer HARD STOP before the handoff report. The handoff artifact `runs/{runId}.md` is written UNCONDITIONALLY at Step 6b (per SKILL.md line 60 MUST — operator must have an audit trail on every terminal outcome), and Step 6c writes a separate `runs/{runId}-registry-stale.md` skeleton with an actionable manual-recovery recipe when the registry PATCH failed. Only AFTER both artifacts are written does the skill exit non-zero. The registry state is still stale (operator MUST manually resolve via `pac data update` or Portal), but now the operator has a durable diagnostic pointing them at the recovery path — a significant improvement over the prior behavior of throwing uncaught before any artifact was written.

#### 6b. Write handoff report

`runs/{runId}.md` in the operator's cwd. Per PLX-12 (SESSION 15 Wave 4), the template shown below is the SHAPE ONLY — the skill MUST substitute every `{token}` before writing. Direct `Set-Content` of the template verbatim would ship an audit-trail artifact with literal `{runId}` etc. text (mandatory audit-trail per SKILL.md line 60 would be corrupted).

Skill substitution block (immediately before `Set-Content`):

```powershell
$template = @'
<TEMPLATE-CONTENT-HERE — literal below>
'@
$report = $template `
  -replace '\{runId\}',           $runId `
  -replace '\{customerId\}',      $customerId `
  -replace '\{tenantId\}',        $tenantId `
  -replace '\{tenancyModel\}',    $tenancyModel `
  -replace '\{profile\}',         $profile `
  -replace '\{startedAt\}',       $run.StartedOn.ToString('o') `
  -replace '\{completedAt\}',     $run.CompletedOn.ToString('o') `
  -replace '\{duration\}',        ("{0:hh\:mm\:ss}" -f ($run.CompletedOn - $run.StartedOn)) `
  -replace '\{l2Base\}',          $l2Base `
  -replace '\{amount\}',          $costMonthly `
  -replace '\{escalation notes if any\}', $escalationNotes `
  -replace '\{timestamp\}',       (Get-Date -Format 'o') `
  -replace '\{version\}',         $deployedBffVersion `
  -replace '\{URL\}',             $customerFacingUrl
Set-Content -Path "runs/$runId.md" -Value $report
```

Template shape:

```markdown
# Provisioning Run {runId}

- **Customer**: {customerId}
- **Tenant**: {tenantId}
- **Tenancy Model**: {tenancyModel}
- **Profile**: {profile}
- **Started**: {startedAt}
- **Completed**: {completedAt}
- **Wall-clock duration**: {duration}
- **Status**: Completed / Failed / Cancelled / Quarantined (per `RunStatus` enum; no `Drifted` — drift surfaces as `Failed` + rejection code)
- **L2 run URL**: {l2Base}/api/runs/{runId}

## Handler outcomes

| # | Handler | Status | Duration | Notes |
|---|---|---|---|---|
| 1 | H0 preflight | Succeeded | 8.2s | quota OK, DNS OK |
| 2 | H1 rg-provision | Succeeded | 12s | |
| 3 | H2a bicep-apply | Succeeded | 28m 14s | |
| ... | ... | ... | ... | ... |
| N | H13 acceptance | Succeeded | 1m 32s | 6/6 traps clear, 5/5 invariants pass |

## Traps verified (T1-T6)

- T1 (keyVaultReferenceIdentity == UAMI): ✅
- T2 (Dataverse App User for MI): ✅
- T3 (UAMI Graph app-role parity, 14/14): ✅
- T4 (Exchange ApplicationAccessPolicy, 2 entries): ✅
- T5 (both slot MIs KV RBAC): ✅ (structurally impossible post-Phase C UAMI)
- T6 (SPE container-type conf-client cert): ✅

## Invariants verified (I1-I5)

- I1 (no hardcoded tenant): ✅
- I2 (AI Search tenantId filter): ✅
- I3 (Cosmos partition-key predicate): ✅
- I4 (SPE container ID from ITenantContainerResolver): ✅
- I5 (Graph per-tenant token): ✅

## Cost snapshot

- Estimated monthly: ${amount}/mo
- vs envelope: within limits / +N% over ({escalation notes if any})

## Registry state

`sprk_dataverseenvironment` for customerId={customerId}:
- sprk_provisionedon: {timestamp}
- sprk_bffversion: {version}
- sprk_solutionversion: {version}
- sprk_setupstatus: Ready

## Deviations / notes

{any manual gates encountered, quota bumps requested, drift detected, etc.}

## Next steps

- Notify customer admin: {URL to send them / instructions}
- Post-provision smoke tests: {list from customer-comms template U-CB-01}
- Monitor for 24h via App Insights: {URL}
```

#### 6c. Registry-stale diagnostic (Bucket B HIGH#10 SESSION 18)

If `$script:RegistryStale = $true` (Step 6a failed on BOTH the MCP call AND the raw Web API fallback), write a separate `runs/{runId}-registry-stale.md` skeleton with an actionable manual-recovery recipe. This file supplements — does NOT replace — the mandatory `runs/{runId}.md` handoff artifact written at Step 6b.

```powershell
if ($script:RegistryStale) {
  $staleReport = @"
# Registry Stale — Run $runId

**⚠ MANUAL RECOVERY REQUIRED**

The provisioning run reached RunStatus.Completed successfully, but the operator-side
Step 6a Dataverse registry PATCH FAILED. The customer's `sprk_dataverseenvironment`
row is missing the promoted Ready-state columns (sprk_provisionedon, sprk_bffversion,
sprk_solutionversion, sprk_azuresubscriptionid, sprk_resourcegroupname,
sprk_appservicename, sprk_keyvaultname, sprk_containertypeid, sprk_ClientCacheBustToken).

The customer's Azure resources are provisioned correctly and the L2 control-plane
has released the I5 concurrency guard (`sprk_currentrunid` via ICustomerRunGuard.
ReleaseAsync per Bucket B HIGH#6/#7 SESSION 18). Only the operator-side registry
PATCH failed. Customer-facing functionality works; only the operator dashboards and
downstream automation that queries these columns are affected.

## Diagnostic

$($script:RegistryStaleDiagnostic)

## Manual Recovery (choose ONE)

### Option A — pac data update (recommended for CLI operators)
``````powershell
pac data update `
    --environment $dvUrl `
    --entity sprk_dataverseenvironment `
    --record-id $environmentId `
    --data '{
      "sprk_provisionedon":       "$completedAtIso",
      "sprk_bffversion":          "$deployedBffVersion",
      "sprk_solutionversion":     "$deployedSolutionVer",
      "sprk_azuresubscriptionid": "$azureSubId",
      "sprk_resourcegroupname":   "$rgName",
      "sprk_appservicename":      "$appServiceName",
      "sprk_keyvaultname":        "$kvName",
      "sprk_containertypeid":     "$containerTypeId",
      "sprk_ClientCacheBustToken":"$cacheBustToken"
    }'
``````

### Option B — Power Apps Portal (recommended for GUI operators)
1. Open https://make.powerapps.com → your environment
2. Navigate: Tables → sprk_dataverseenvironment → row `$environmentId`
3. Edit the columns listed above using the values from ``runs/$runId.md`` § Deployed Versions
4. Save

### Option C — Skill re-invocation with -ResumeRegistryPatch flag
(Not yet implemented; add to backlog if this failure recurs.)

## Post-recovery verification

After applying either recovery option:
``````powershell
mcp__dataverse__read_query(query = "<fetch><entity name='sprk_dataverseenvironment'><attribute name='sprk_provisionedon' /><filter><condition attribute='sprk_dataverseenvironmentid' operator='eq' value='$environmentId' /></filter></entity></fetch>")
``````
Expect ``sprk_provisionedon != null``. If null, retry the recovery.

## Do NOT

- **Do NOT touch** `sprk_setupstatus` (already set to Ready by L2 H13)
- **Do NOT touch** `sprk_currentrunid` (already released by ICustomerRunGuard per Bucket B HIGH#6/#7)
- **Do NOT touch** `sprk_tenantid` (I1 invariant — set at placeholder-create, NEVER re-writable)

## Escalation

If manual recovery fails repeatedly, file a GitHub Issue with:
- This file (``runs/$runId-registry-stale.md``)
- The handoff report (``runs/$runId.md``)
- The Dataverse error message from the recovery attempt
"@
  Set-Content -Path "runs/$runId-registry-stale.md" -Value $staleReport -Encoding utf8
  Write-Warning "Registry-stale diagnostic written to runs/$runId-registry-stale.md — MANUAL RECOVERY REQUIRED. Handoff report at runs/$runId.md is complete."
}
```

#### 6d. Final summary to operator

```
✅ PROVISIONING COMPLETE

  Customer:  {customerId}
  Run ID:    {runId}
  Duration:  {duration}
  Status:    Ready

  Handoff report: runs/{runId}.md

  Registry updated: sprk_dataverseenvironment.sprk_setupstatus = Ready

  Post-completion checklist:
    [ ] Send customer welcome email (template docs/deployment/customer-comms/U-CB-01)
    [ ] Verify first user can sign in and load workspace
    [ ] Confirm cost drift alerts configured in Azure
    [ ] Update project #2 (portfolio board) with the new customer entry
```

**Bucket B HIGH#10 SESSION 18**: When `$script:RegistryStale = $true`, replace the final summary above with the WARNING variant:

```
⚠ PROVISIONING COMPLETE (REGISTRY STALE)

  Customer:  {customerId}
  Run ID:    {runId}
  Duration:  {duration}
  Status:    Ready (L2), Registry PATCH FAILED (operator-side)

  Handoff report: runs/{runId}.md
  Registry-stale diagnostic: runs/{runId}-registry-stale.md

  MANUAL RECOVERY REQUIRED — see runs/{runId}-registry-stale.md for the
  pac data update / Portal recipe. Customer-facing functionality is
  operational; only operator dashboards + downstream automation affected.
```

Then exit non-zero (code 5, distinct from the exit-4 gate-timeout and exit-3 quarantine paths at Step 4-5) AFTER both artifacts are written:

```powershell
if ($script:RegistryStale) {
  exit 5
}
```

---

### Step 7: Postmortem — write `lessons-learned.md` (MANDATORY) — added by task 203c per punch-list row A04

Runs UNCONDITIONALLY after Step 6 (Completion Handoff) regardless of outcome — `Completed`, `Failed`, `Cancelled`, `Quarantined`, or manual-abort (no `Drifted` state — drift surfaces as `Failed` + upgrade-drift-detected rejection code). Written BEFORE the run folder is committed to git so the postmortem is captured with the same commit as the artifacts. Consumes the 203a-authored template at [`provisioning-runs/_templates/lessons-learned.md`](../../provisioning-runs/_templates/lessons-learned.md). Skipping this step silently regresses the two-level lessons process (in-flight direct-apply per root CLAUDE.md §7 wrap-up + this per-run postmortem).

Trigger conditions (each writes a distinct postmortem):
- `Completed` (H13 acceptance passed; sprk_setupstatus=Ready reached) — capture what worked + manual gates encountered + recommendations
- `Failed` (any handler unrecoverable per §4C taxonomy; includes drift-detected via `upgrade-drift-detected` rejection code) — capture root cause + fix location + blocks-future-runs flag
- `Cancelled` (operator called POST /api/runs/{id}/cancel) — capture stop point + rationale
- `Quarantined` (rollback classified `NeedsHumanIntervention`) — capture quarantine reason + owner
- Manual abort (operator stopped skill session mid-run without calling cancel) — capture stop point + rationale

#### 7a. Copy template + prefill run metadata

```powershell
$runDir       = "provisioning-runs/$customerId-$runId"
$lessonsPath  = Join-Path $runDir 'lessons-learned.md'
$templatePath = Join-Path (git rev-parse --show-toplevel) 'provisioning-runs/_templates/lessons-learned.md'

Copy-Item -Path $templatePath -Destination $lessonsPath -Force

# Substitute template placeholders with actual run metadata
$content = Get-Content $lessonsPath -Raw
$content = $content -replace '\{customerId\}', $customerId
$content = $content -replace '\{runId\}', $runId
$content = $content -replace '\{operatorUpn\}', $operatorUpn
$content = $content -replace '\{ts\}', (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')
Set-Content -Path $lessonsPath -Value $content
```

#### 7b. Interactive postmortem (or batch mode: `intake.postmortemFile`)

Present the operator with each template section and collect responses. In batch mode, honor `$script:BatchPostmortemFile` (bound at Step 1.0 from `intake.postmortemFile`).

**Batch-mode postmortem dispatch (BAT-09, SESSION 16)**:

```powershell
if ($script:SkipInteractiveIntake) {
  if ($script:BatchPostmortemFile) {
    $postmortemAbs = if ([System.IO.Path]::IsPathRooted($script:BatchPostmortemFile)) {
      $script:BatchPostmortemFile
    } else {
      Join-Path (git rev-parse --show-toplevel) $script:BatchPostmortemFile
    }
    if (-not (Test-Path $postmortemAbs)) {
      $diag = @{ runId = $runId; reason = "postmortemFile '$postmortemAbs' not found on disk"; remedy = 'Author the file at the path in intake.postmortemFile OR omit the field to auto-generate a minimum lessons-learned.md' } | ConvertTo-Json
      Set-Content -Path "runs/$runId-postmortem-invalid.json" -Value $diag
      Write-Error "[skill] Batch HARD STOP (BAT-09): intake.postmortemFile references '$postmortemAbs' which does not exist. Diagnostic: runs/$runId-postmortem-invalid.json"
      exit 6
    }
    # Validate required sections (mirror interactive template's H2 headings)
    $required = @('What went right','What went wrong','Recommendations for next run','Sign-off')
    $content  = Get-Content -Raw -Path $postmortemAbs
    $missing  = @()
    foreach ($h in $required) {
      if ($content -notmatch "(?im)^##\s+$([regex]::Escape($h))") { $missing += $h }
    }
    if ($missing.Count -gt 0) {
      $diag = @{ runId = $runId; postmortemFile = $postmortemAbs; missingSections = $missing; remedy = 'Add the missing ## headings, or omit intake.postmortemFile to auto-generate the minimum shape' } | ConvertTo-Json -Depth 4
      Set-Content -Path "runs/$runId-postmortem-invalid.json" -Value $diag
      Write-Error "[skill] Batch HARD STOP (BAT-09): intake.postmortemFile is missing required sections: $($missing -join ', '). Diagnostic: runs/$runId-postmortem-invalid.json"
      exit 6
    }
    # Copy verbatim + append auto-populated metadata (git-sha, INDEX.md lessons-count, run outcome)
    Copy-Item -Path $postmortemAbs -Destination $lessonsPath -Force
    $gitSha = git rev-parse HEAD
    $auto = @"

---

## Auto-populated metadata (BAT-09)

- **runId**: $runId
- **customerId**: $customerId
- **outcome**: $runOutcome
- **git-sha**: $gitSha
- **written-at**: $(Get-Date -Format 'o')
- **source-postmortem**: $script:BatchPostmortemFile (validated + copied verbatim by skill Step 7b)
"@
    Add-Content -Path $lessonsPath -Value $auto
    Write-Host "  [BATCH] Postmortem copied from $script:BatchPostmortemFile + metadata appended." -ForegroundColor Cyan
  } else {
    # No postmortemFile — auto-generate minimum shape from run outcome
    $minimum = @"
# Lessons Learned — $customerId / $runId

## What went right
- Run reached terminal state '$runOutcome' without manual gate escalation beyond design tolerance.

## What went wrong
- No operator-authored lessons for this batch run. If lessons DO exist for this run, an operator SHOULD amend this file post-hoc via a follow-up commit citing the runId.

## Recommendations for next run
- (none — auto-generated postmortem; consider providing intake.postmortemFile on future batch runs for higher-fidelity lessons capture)

## Sign-off
- Author: batch-mode auto-generation (BAT-09 auto-minimum path)
- Reviewer: pending (operator should review + amend if lessons emerge)
- git-sha: $(git rev-parse HEAD)
- written-at: $(Get-Date -Format 'o')
"@
    Set-Content -Path $lessonsPath -Value $minimum
    Write-Host "  [BATCH] Auto-generated minimum lessons-learned.md (no intake.postmortemFile supplied)." -ForegroundColor Cyan
  }
} else {
  # INTERACTIVE — see below (operator prompt per template section)
}
```

Sections (per template):
- **What went right** — 3-5 concrete bullets citing handler + timestamp
- **What went wrong** — normalized `### Lesson L01/L02/...` shape with Symptom / Root cause / Fix applied / Landing spot / Blocks future runs / Punch-list class
- **New prereqs to codify** — proposed additions to `PROVISIONING-PREREQUISITES.md` + `prereqs.yaml` (Step 0.5 picks them up automatically on the NEXT run)
- **New patterns to add** — proposed additions to `.claude/patterns/provisioning/`
- **Recommendations for next run** — concrete actionable items (avoid vague aspirations)
- **Cross-run pattern** — first-observed / occurrence-count / recommended-promotion
- **Sign-off** — author + reviewer + git-sha

The Step 0.5 iteration result (which prereqs PASSed, which were skipped via `-SkipStep0_5` / `skipExternalPrereqs`, which FAILed) MUST be summarized under **What went right** or **What went wrong** as appropriate — this makes Step 0.5 outcomes visible in the cross-run audit corpus.

#### 7c. Update `provisioning-runs/INDEX.md`

Append this run's lesson-count so the cross-run audit slash command `/audit-provisioning-lessons` (planned; task 203-followup) can roll up recurring themes.

```powershell
$lessonCount = (Select-String -Path $lessonsPath -Pattern '^### Lesson ').Count
$indexRow = "| $customerId-$runId | $(Get-Date -Format 'yyyy-MM-dd') | $runOutcome | $lessonCount |"
Add-Content -Path (Join-Path (git rev-parse --show-toplevel) 'provisioning-runs/INDEX.md') -Value $indexRow
```

#### 7d. Report + commit gate

```
POSTMORTEM CAPTURED

  Lessons written: provisioning-runs/{customerId}-{runId}/lessons-learned.md
  Lesson count:    {N}
  INDEX.md row:    | {customerId}-{runId} | {date} | {outcome} | {N} |

  Next step: commit the entire {customerId}-{runId}/ folder (Step 6b handoff report + this postmortem + all artifacts). Once committed, this run's postmortem contributes to the cross-run audit corpus (/audit-provisioning-lessons roll-up).
```

**MANDATORY** — skipping is FORBIDDEN even for successful runs. An operator explicitly declining ("no meaningful lessons for this run") still writes a 3-line lessons-learned.md stating that + commits it (audit trail). Silent skip regresses the entire two-level lessons process.

---

## Fallback Matrix

> Per spec §4.3a.5 + task 076. Three primary failure modes with decision-tree fallbacks. Each subsection: **primary command → fallback command → escalation trigger**.

### F1. Dataverse MCP disconnect

**Symptom**: `mcp__dataverse__*` calls return connection error, timeout, or "MCP server not connected."

**Impact**: Cannot query gate state via MCP; cannot update `sprk_dataverseenvironment` via MCP in Step 6.

**Decision tree**:

```
IF mcp__dataverse__* call fails with connection error:

  1. DO NOT attempt reconnect loop — MCP disconnects require the operator to
     re-authenticate the connector in VS Code / Claude Code settings, which
     is out-of-band. A skill-side reconnect will just retry the failed call.

  2. Log the fallback: "⚠ MCP disconnect at Step {N}; switching to pac data fallback"

  3. Fallback A (preferred if pac is authed): pac data
       # Read
       pac data query --entity sprk_dataverseenvironment `
         --filter "sprk_customerid eq '{customerId}'" `
         --select sprk_dataverseenvironmentid,sprk_currentrunid,sprk_setupstatus

       # Write (Step 6 registry update — setupstatus 2 = Ready per EnvironmentSetupStatus enum)
       pac data update --entity sprk_dataverseenvironment `
         --id {envRecordId} `
         --data '{"sprk_provisionedon":"{timestamp}","sprk_setupstatus":2,"sprk_currentrunid":null}'

  4. Fallback B (if pac unavailable OR command shape not supported): raw Web API PS
       # NOTE: registry updates target the REGISTRY env (spaarkedev1 for dev),
       # NOT the customer's just-provisioned env. `sprk_dataverseenvironment`
       # is the central catalog per operator memory feedback_no_central_managing_env_yet.
       $dvUrl = "https://spaarkedev1.crm.dynamics.com"  # registry env
       $dvToken = az account get-access-token --resource $dvUrl --query accessToken -o tsv
       $body = @{
         sprk_provisionedon = "{timestamp}"
         sprk_setupstatus   = 2         # 2 = Ready per EnvironmentSetupStatus enum
         sprk_currentrunid  = $null
       } | ConvertTo-Json
       Invoke-RestMethod `
         -Uri "$dvUrl/api/data/v9.2/sprk_dataverseenvironments({envRecordId})" `
         -Method PATCH `
         -Headers @{
           Authorization = "Bearer $dvToken"
           "OData-Version" = "4.0"
           "If-Match" = "*"
         } `
         -Body $body -ContentType "application/json"

  5. NOTE in handoff report which fallback was used + reason (MCP was
     disconnected). This becomes an operator action item — re-auth the MCP
     connector in VS Code settings BEFORE the next provisioning run.
```

**Escalation trigger**: BOTH primary + Fallback A + Fallback B fail. This is rare and indicates a broader Dataverse-connectivity issue (network, cert, or Dataverse-side outage). STOP + escalate; do NOT mark the run complete. Registry becomes stale (`CompleteButRegistryStale` state) until operator manually reconciles.

**Design intent**: MCP disconnect is a normal occurrence per our own experience (2026-08-14, 2026-08-15). The skill degrades gracefully via `pac data` (which uses the same `pac auth` context — no re-authentication needed) or raw Web API (which reuses the `az` token operator already has). No user-facing prompt required.

---

### F2. `az` token expiry mid-run

**Symptom**: L2 REST call returns `401 Unauthorized` with `WWW-Authenticate: Bearer error="invalid_token"`; token acquired at start of run has aged past ~1 hour lifetime.

**Impact**: Skill loses ability to advance the run until token is refreshed.

**Decision tree**:

```
IF Invoke-RestMethod against $l2Base returns HTTP 401:

  1. DO NOT re-authenticate the operator — they are already logged in
     (`az login` succeeded at Step 0). Only refresh the L2-audience token.

  2. Refresh via:
       $token = az account get-access-token `
         --resource "api://spaarke.com/provisioning-controlplane-$env" `
         --query accessToken -o tsv

     `az` uses cached refresh tokens; this is silent + fast (<2s) if the
     operator's session is still valid.

  3. Retry the failed L2 call ONCE with the new token.

  4. IF retry also returns 401:
     - Operator's SSO session may have expired (rare inside a single skill
       invocation, but possible on long-running runs with human gates).
     - PAUSE the poll loop; prompt operator:
         "⚠ az token refresh failed. Please run 'az login' in a separate
          terminal and confirm here when done. The run continues in L2
          — it is not lost. Type 'refreshed' to resume, or 'abandon' to
          quarantine."
     - On 'refreshed' → re-acquire token + resume poll from where it stopped.
     - On 'abandon' → invoke POST /api/runs/{id}/quarantine with reason
       "operator session expired; abandoning skill session."

  5. Log every refresh event in the handoff report — repeated refreshes on
     the same run are a signal to shorten the run OR investigate token
     lifetime configuration on the L2 app-reg.
```

**Escalation trigger**: 3 consecutive refreshes fail. Indicates operator's `az` login is broken (tenant policy, MFA revocation, etc.). Skill hard-stops; operator addresses AAD state before re-invoking.

**Design intent**: Token expiry is expected on long runs (Model 2 full provisioning can exceed 1 hour). Auto-refresh keeps the operator out of the loop for the common case. The prompt is a last resort for genuinely-expired SSO sessions.

---

### F3. L2 API unreachable

**Symptom**: `POST /api/runs`, `GET /api/runs/{id}`, or `POST /api/runs/{id}/resume` returns 5xx (500, 502, 503, 504) OR the request times out (connection refused, DNS failure, gateway timeout).

**Impact**: Cannot advance the run through the skill. L2 continues running its state-reconciler independently (per FR-22 + FR-23 I6 crash-recovery) — the run is NOT lost; the skill is just cut off from steering it.

**Decision tree**:

```
IF L2 call returns 5xx OR times out:

  1. First occurrence: retry ONCE with 30s backoff. Transient errors
     (Azure ILB blip, App Service cold-start, Cosmos throttling) usually
     recover in <60s.

  2. If second attempt also fails, DO NOT auto-retry further. Auto-retry
     loops hide the outage from the operator + waste their time.

  3. ESCALATE IMMEDIATELY to the operator:

       🚨 L2 API UNREACHABLE

         Endpoint:  {failed URL}
         Response:  {status code + first 200 chars of body OR error}
         Elapsed:   {time since first failure}

         The provisioning run continues in L2 — your work is NOT lost.
         L2's state-reconciler (I6 crash-recovery) will resume orphan
         runs when L2 is back up.

         DIAGNOSTIC ACTIONS:
           1. Check L2 App Service status:
                az webapp show --resource-group rg-spaarke-{env} `
                  --name spaarke-provisioning-{env} --query state
              (expect "Running")

           2. Check L2 /healthz:
                curl -sf https://spaarke-provisioning-{env}.azurewebsites.net/healthz
              (expect 200)

           3. Check L2 App Insights for failures (last 15m):
                {App Insights URL for this L2 instance}

           4. Check Cosmos DB availability (partition rebalancing?):
                az cosmosdb show --resource-group rg-spaarke-{env} `
                  --name cosmos-spaarke-{env} --query provisioningState

           5. Escalate to on-call if outage exceeds 15 min:
                {on-call URL / channel}

         RESUME OPTIONS:
           - When L2 recovers, re-invoke:
                /provision-environment {customerId}
             The skill detects the in-progress run + resumes at Step 4 poll.
           - Do NOT create a new run for the same customerId — L2 will
             return 409 via optimistic concurrency lock on sprk_currentrunid.

  4. Write partial handoff report with status "SkillDetached" — captures
     what the skill saw before the outage. When operator re-invokes and
     the run reaches terminal state, a second (final) report is written
     at runs/{runId}.md (superseding the partial).
```

**Escalation trigger**: Any 5xx or timeout that doesn't clear after the first backoff. L2 outage is genuinely rare + operator-actionable.

**Design intent**: L2 owns run state; the skill is a thin driver. When L2 is unavailable, the skill can't drive but the state persists in Cosmos. I6 (FR-23) crash-recovery on L2 restart resumes orphan runs automatically. The skill's job is to surface the outage clearly + tell the operator how to check + reconnect — NOT to try to work around L2.

---

### Cross-references

- **Step 4 (Execute Loop)** — F1 (MCP not needed here) / F2 (token auto-refresh applies during poll) / F3 (L2 unreachable during poll) all in scope
- **Step 5 (Manual Gate Handling)** — F2 applies if operator's session expires during a long gate wait (Model 2 admin consent, SPE 24h)
- **Step 6 (Completion Handoff)** — F1 (MCP registry update fallback) is the primary use case; F2 (token refresh) rare but possible if the run took >1h

---

## Tool Matrix (per spec §4.3a.1 — 15 tools)

| # | Capability | Primary | Fallback |
|---|---|---|---|
| 1 | Read design + spec + POML | Read / Glob / Grep | — |
| 2 | Invoke PowerShell scripts | `PowerShell` tool | `Bash` + `pwsh -File` |
| 3 | Invoke bash tooling | `Bash` tool | — |
| 4 | Call L2 REST API | `WebFetch` (bearer from #5) OR `Bash` + `curl` + `az account get-access-token` | `PowerShell` + `Invoke-RestMethod` |
| 5 | AAD bearer for L2 | `az account get-access-token --resource api://spaarke.com/provisioning-controlplane-{env}` | Interactive `az login` first |
| 6 | Read Dataverse | `mcp__dataverse__read_query`, `mcp__dataverse__search` | `pac data` OR raw Web API |
| 7 | Write Dataverse | `mcp__dataverse__update_record` | `pac data` OR raw Web API PATCH |
| 8 | Read Azure resource state | `Bash` + `az resource show / az keyvault / az webapp` | Azure MCP if configured |
| 9 | Read Graph | `Bash` + `az ad / az rest` against Graph endpoints | Graph SDK script |
| 10 | Read Cosmos | `Bash` + `az cosmosdb sql query` | `Invoke-RestMethod` against Cosmos SQL API |
| 11 | File upload to SPE (H13 sample) | `Bash` + `curl` with Graph app-only token | `pnp` CLI OR Graph SDK script |
| 12 | Real-time run status polling | `WebFetch` / `curl` loop against `GET /api/runs/{id}` | — |
| 13 | Structured handoff report | `Write` (markdown to `runs/{runId}.md`) | — |
| 14 | Task tracking | `TodoWrite` tool | — |
| 15 | Multi-agent orchestration (rare) | `Agent` tool with `researcher` / `general-purpose` subagent | — |

---

## Auth Flow

**Identity**: operator's own AAD (`az login`) per NFR-11. NEVER a service principal.

**Role**: `Operator` app-role on the control-plane app-reg `api://spaarke.com/provisioning-controlplane-{env}`. Assigned via:

```
az ad app show --id api://spaarke.com/provisioning-controlplane-dev --query "id"
# → objectId of the app-reg's SP
az rest --method POST --uri "https://graph.microsoft.com/v1.0/servicePrincipals/{spObjId}/appRoleAssignments" `
  --body '{ "principalId":"{operatorObjId}", "resourceId":"{spObjId}", "appRoleId":"{operatorRoleGuid}" }'
```

**Token**: acquired once per run, refreshed on 401:

```powershell
$token = az account get-access-token `
  --resource "api://spaarke.com/provisioning-controlplane-{env}" `
  --query accessToken -o tsv
```

Lifetime ~1 hour. Fallback matrix handles mid-run expiry.

**Model 2 exception**: H0.5 consent-callback endpoint (`POST /api/onboarding/consent-callback`) is anonymous + HMAC-verified — the customer admin authenticates to THEIR tenant via Microsoft, not to Spaarke's control plane. The skill does NOT participate in this exchange directly; L2 auto-detects the HMAC-signed callback.

---

## Dry-Run Mode

Support a `--dry-run` flag on the slash command:

```
/provision-environment trial-acme-2026-08-18 --dry-run
```

Behavior differences:
- Step 0 unchanged (still run prereqs; dry-run doesn't skip environment checks)
- Step 1 unchanged (still collect intake)
- Step 2 invokes L2 with `mode: "preflight-dry-run"` — H0 runs quota/DNS/reachability checks WITHOUT reserving any resources (Cosmos writes = read-only assertion of state; ARM calls use `az resource show` not `az deployment`)
- Step 3 confirmation gate still requires "proceed with provisioning" but prints "(DRY-RUN)" prefix; on proceed:
- Step 4 does NOT enqueue H1-H14 — instead L2 returns a JSON plan (`GET /api/runs/{runId}/plan-preview`) enumerating what WOULD run
- Step 5 skipped (no gates in dry-run)
- Step 6 writes handoff report labeled `runs/{runId}-DRYRUN.md` and does NOT touch `sprk_dataverseenvironment`

Dry-run is intended for pre-flight validation before a real customer deployment (e.g., "prove we can provision trial-acme without actually doing it").

---

## Troubleshooting

| Issue | Cause | Resolution |
|---|---|---|
| Step 0c returns 403 on role probe | Operator not granted `Operator` app-role on `api://spaarke.com/provisioning-controlplane-{env}` | Ask a control-plane admin to run the `az rest` app-role assignment (see Auth Flow) |
| Step 0c token acquisition fails: `AADSTS500011` | Resource URI wrong OR app-reg not exposed in operator's tenant | Verify `az ad app show --id api://spaarke.com/provisioning-controlplane-{env}` returns a value; if not, wrong env |
| Step 1 customerId probe returns 500 | L2 read-side broken; database issue | Check L2 App Insights + escalate to on-call — do NOT bypass |
| Step 2 H0 times out (>60s) | Azure quota query hung OR L2 stuck | Check L2 logs; may need to abort + retry; occasional Azure quota API slowness |
| Step 4 poll returns 401 mid-run | Token expired | Auto-refresh via `az account get-access-token` (see Fallback Matrix); retry the poll |
| Step 4 handler fails with `QuarantineRequired` | Handler cleanup impossible; state indeterminate | STOP — file `POST /api/runs/{runId}/clear-quarantine` with reason after root-cause analysis. NEVER retry without clear-quarantine |
| Step 5 gate URL 404 on customer admin's browser | Model 2 admin consent URL malformed | Check H0.5 output; may need to reconstruct manually with correct redirect_uri |
| Step 6 MCP update fails | MCP disconnected between preflight + completion | Fallback matrix → `pac data update` (see Fallback Matrix) |
| Skill exits before Step 6 (session timeout, network blip) | L2 continues asynchronously; run state persists in Cosmos | Re-invoke `/provision-environment {customerId}` — skill detects the in-progress run via `GET /api/runs?customerId={id}` and resumes at Step 4 poll |

---

## Related Skills

| Skill | When to use instead |
|---|---|
| `/deploy-new-release` | Deploying a new release to existing environments (different lifecycle stage — provisioning is customer onboarding; deploy-new-release is application updates) |
| `/dataverse-mcp-usage` | Reference for MCP tool call patterns used in Step 6 registry update |
| `/adr-check` | Auto-invoked by task-execute when this skill is authored/modified |
| `/code-review` | Auto-invoked by task-execute when this skill is authored/modified |
| `/conflict-check` | Optional pre-flight before any BFF-touching provisioning follow-up |

---

## Related Documentation

| Document | Purpose |
|---|---|
| [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) | Full operator runbook including §12 interim manual sequence this skill supersedes |
| [`projects/customer-provisioning-orchestration-r1/spec.md`](../../../projects/customer-provisioning-orchestration-r1/spec.md) | Authoritative spec — FR-25, §4.3a, NFR-11 |
| [`projects/customer-provisioning-orchestration-r1/design.md`](../../../projects/customer-provisioning-orchestration-r1/design.md) | Design v3.3 — §4.3a operator toolchain, §4B silent-fail traps, §4C rollback taxonomy, §4D tenant-isolation invariants, §14A upgrade model |
| [`docs/deployment/version-compatibility-matrix.md`](../../../docs/deployment/version-compatibility-matrix.md) | H0 upgrade-mode preflight consults this |
| [`docs/deployment/customer-comms/`](../../../docs/deployment/customer-comms/) | 6 U-CB customer communication templates (welcome, gate URLs, escalation) |
| [`.claude/skills/deploy-new-release/SKILL.md`](../deploy-new-release/SKILL.md) | Reference model this skill's UX pattern derives from |

---

## Tips for AI

- **Prereqs are non-negotiable.** Do NOT proceed past Step 0 if any check fails — silent tool-version mismatches are the #1 cause of silent-fail traps.
- **NEVER default a `tenantId`.** The `Register-EntraAppRegistrations.ps1:63` fix (`1834b77bc`) established that hardcoded default tenant is an I1 invariant violation. Ask; don't assume.
- **The confirmation gate phrase is load-bearing.** "y" and "yes" are insufficient — the operator must type `proceed with provisioning` literally. This is captured in the audit trail per NFR-11.
- **Poll, don't reach.** Never invoke handlers directly or query BFF Service Bus. L2 owns orchestration; the skill is a thin driver.
- **Manual gates require re-verification, not trust.** Always call `POST /api/runs/{id}/resume` and let L2 re-verify (Dataverse, Graph, Azure state). The operator's assertion "I did it" is NOT sufficient.
- **Auto-retry hides diagnostics.** On `Failed`, present the failure + operator options; do NOT auto-retry. Operators need to see the failure to root-cause.
- **`QuarantineRequired` is a hard stop.** Never invoke `clear-quarantine` on behalf of the operator; the reason field is auditable + requires human accountability.
- **Handoff report is durable.** Write `runs/{runId}.md` on every completion — success OR failure OR quarantine. The report is the audit trail + the resumption baseline.
- **MCP disconnect is common** (we experienced this 2026-08-14, 2026-08-15). The fallback matrix handles it. Do not treat MCP disconnect as an error — it's expected.
- **The skill is idempotent at the intake level.** If the operator re-invokes with the same `customerId`, the skill detects the existing run + resumes rather than starting fresh. The state lives in Cosmos, not the skill session.
- **BINDING pre-check protocol** (FR-35): before removing any KV alias / fallback spelling, pre-check the LIVE App Service + KV + Dataverse-persisted config. Root CLAUDE.md §10 canonical secret-catalog manifest is the source of truth.
- **KV credential-lifecycle rule** (updated 2026-08-27 SESSION 13 task 199 for E-3 CLOSED reality per `spaarke-auth-v4-dataverse-MI` task 033 completion 2026-08-24): **`BFF-API-ClientSecret` is GONE** — both KV copies + all 4 App Service settings deleted; credential order pinned to `[ManagedIdentityFederated]` with `RequireSecretFreeIdentity=true`. Do NOT re-introduce this secret under any name — `CredentialGuardTests` fails the build on any new `.WithClientSecret(...)` site. H4 **omits** `BFF-API-ClientSecret` unconditionally. Separately, `Dataverse-ClientSecret` never-delete rule STILL in force until 2026-11-23 (auth-v4 owns its retirement). Full rule: `.claude/constraints/provisioning.md` §KV credential lifecycle.

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Step 0 skipped ("just start the run, we know the machine is fine") | Operator confidence + skill impatience | HARD STOP; prereqs are unconditional. Silent tool-version mismatches are the #1 cause of silent-fail traps T1-T6. |
| Confirmation gate bypassed with "y" | Skill accepted a partial phrase | Enforced literal string `proceed with provisioning`. Any other input re-asks. |
| Same-customer concurrent run attempted | Operator forgot the first run is still active | L2 returns 409 via optimistic concurrency on `sprk_currentrunid`. Skill presents the existing run's status + offers to resume/poll rather than starting a second. |
| Handler retried past its retry budget | Auto-retry logic in the skill | REMOVED — the skill never auto-retries. Operator sees failures + decides. |
| Manual gate auto-advanced by trusting operator assertion | Skill said "y" advances the run without L2 re-verifying | ALWAYS call `POST /api/runs/{id}/resume`; L2 re-verifies the underlying condition (Dataverse / Graph / Azure state). If verification fails, the run stays at `WaitingOnGate` regardless of operator input. |
| `Quarantined` run silently ignored by operator (walked away) | Skill session ended before quarantine surfaced | Quarantine is written to Cosmos + surfaces on next `/provision-environment {customerId}` invocation. Skill presents it as the first order of business + refuses to start new runs until cleared. |
| Handoff report not written on failure | Skill treated failure as "no report needed" | Report is written on ALL terminal RunStatus values (Completed, Failed, Cancelled, Quarantined — no Drifted; drift surfaces as Failed + rejection code). Failure reports capture the failure mode + diagnostic + resumption instructions. |
| Registry update via MCP fails silently — run marked complete but registry stale | MCP disconnect between preflight + completion; skill didn't check | Fallback matrix triggers immediately on MCP failure; registry MUST be updated before completion is reported. If BOTH MCP + fallback fail, run is marked `CompleteButRegistryStale` and operator must manually update via `pac data update`. |
| Token expires mid-run; skill fails hard | No auto-refresh | Fallback matrix documents `az account get-access-token` auto-refresh on 401 (see Fallback Matrix section, task 076 owns). |
| L2 unreachable mid-run — skill panics | No graceful degradation | Fallback matrix documents escalation + resume-from-Cosmos-state pattern; L2's crash-recovery (I6) re-runs orphaned runs on restart. |

---

*This skill is the operator's single entry point to the customer-provisioning platform. It wraps the L2 REST API — it does not reimplement provisioning logic. The state machine lives in Cosmos; the handlers live in BFF; this skill is thin UX driving it all.*

---

## Fresh-Sub Automation Gaps (customer-provisioning-orchestration-r1 lessons 2026-08-22)

**Evidence base**: `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` (findings F1-F20, F20a — 2026-08-22 through 2026-08-24 live stand-up)

**Owner directive**: "the expectation for the final delivered solutions is that this process will run E2E with no human interaction. Ultimately the best solution is we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything."

The first live Model 1 Prod stand-up (2026-08-22, sub `cd95fcec-...`) surfaced 9 gotchas Microsoft has quietly introduced since 2024-2025 that break naive "just deploy" flows for fresh Azure subscriptions. Each gotcha is an automation gap this skill MUST absorb before we can claim E2E-no-human-interaction. Step 2.5 (above) is where the automation lives.

| # | Finding | Auto-remediation strategy | Status |
|---|---|---|---|
| F1 | OpenAI model version pins age ~4-6 months; Microsoft blocks new deploys of Deprecating pins | Pre-deploy `az cognitiveservices model list` check; HALT + operator sign-off for pin bump (ADR-020) | MVP: informational; TODO: fully automated with operator confirmation |
| F2 | gpt-5.x family REQUIRES GlobalStandard SKU; module hardcoded 'Standard' broke this | Per-deployment `sku` field in openai module (safe-access default 'Standard') — DONE this session | ✅ Module fix committed in `798f61c9` |
| F3 | East US fresh subs have 0 App Service quota AND Portal auto-denies quota request | Preflight test-deploy in candidate region; auto-fallback to westus2 (Spaarke canonical) | MVP: manual; TODO: automated region auto-selection |
| F4 | West US 2 has NO gpt-5 family; West US 3 has all | Detect gpt-5 GA per region; auto-set `sharedOpenAiLocation` = westus3 when primary is westus2 | MVP: bicepparam manual; TODO: auto-composed |
| F5 | Fresh subs auto-grant mini/embedding TPM generously (500+); frontier tiers (gpt-5.4, gpt-5-pro) = 0 | Query auto-grants; recompose deployment set from what's granted; deferred upgrade path documented | MVP: manual (this session recomposed); TODO: auto-compose from `az cognitiveservices usage list` |
| F6 | `az provider register` reports success but state stays NotRegistered on fresh subs | Retry-verify loop 5 min; HALT with Portal link if not registered | TODO: not implemented |
| F7 | Portal Usage+Quotas provider dropdown empty on fresh subs (only shows providers with existing resources) | Preemptive operator warning + link to https://ai.azure.com Quotas | MVP: informational only |
| F8 | Portal auto-denies fresh-sub quota requests + pushes to Support Ticket | Auto-file via `az support in-subscription tickets create` REST API if Support Plan available | TODO: not implemented — advanced; requires operator to have `Microsoft.Support/*` role |
| F9 | Support Plan availability varies; skill must not queue ticket-dependent actions on plan-less sub | Check Support Plan presence in Step 2.5; downgrade approach if absent | TODO: not implemented |
| F10 | Global resource-name reservations not caught by what-if (Service Bus `-sb` suffix, etc.) — burned 16m35s on this session's first deploy | Run `az {svc} check-name` for every resource with global namespace BEFORE `az deployment sub create` | Bicep fix committed; skill automation TODO |
| F11 | Cognitive Services accounts hold a 3-5 min soft-lock after failed deploys (invisible to `provisioningState`); back-to-back retries fail with RequestConflict even when everything reads Succeeded | Detect RequestConflict on CogSvc writes + linear backoff retry (30s → 90s → 180s → 300s) | TODO: not implemented — burned 3 failed retries this session; 3-min explicit `sleep 180` broke through |
| F12 | `Build-SpaarkeMaster.ps1` was calling `AddSolutionComponent AddRequiredComponents=$false` → managed export had 105 self-referencing "leaky" deps against `solution="Active"`. Fresh env installs failed with 240 total MissingDependency (105 Cat B + 135 Cat A first-party) | Line 138 changed to `$true`; rebuilt in spaarkedev1 → 485 components (was 386); re-export → 77 MissingDep, ALL Category A (Cat B eliminated) | ✅ Script fix committed on this branch; longer-term automation TODO: nightly smoke-install job on rebuilt .zip to a throwaway env + CI assert `MissingDependency solution="Active"` count == 0 |
| F13 | Fresh Production-tier envs do NOT include Power BI Extensions (`msft_PowerBI_Anchor`, publisher solution `msft_PowerBI_Entities`, `isFirstParty="False"`). One SpaarkeMaster env-var-def carries a spurious dep on `powerbimashupparameter` → import fails with 1 unresolved MissingDependency even after F12 fix | Pre-import: intersect `pac application list --environment {env}` with a `Required Applications` manifest; auto-install any missing via `pac application install --application-name {name}` in a loop (each takes ~6 min, polls every 30s). Initial manifest: `msft_PowerBI_Anchor` | ✅ Installed manually this session; automation TODO — introduce `Required Applications` config-driven manifest on H6 solution-import handler; longer-term: fix the spurious dep at source in Build-SpaarkeMaster.ps1 |
| F14 | Fresh Production-tier envs default `organization.maxuploadfilesize = 5,242,880` (5 MB); UniversalDocumentUpload PCF bundle exceeds this. Import fails 5min in with "Webresource content size is too big" | Pre-import: run `pac org list-settings` → compare against Spaarke `Org Settings Contract` → any drift → auto-apply via `pac org update-settings --name maxuploadfilesize --value 25600000`. Idempotent + fast (single API call). Initial contract: `maxuploadfilesize: 25_600_000` (25 MB, matches spaarkedev1) | ✅ Applied manually this session; automation TODO — introduce `Org Settings Contract` config-driven map on H6 solution-import handler |
| F15 | Fresh RBAC-enabled Key Vaults grant NO data-plane access even to subscription Owner. `enableRbacAuthorization=true` KVs require explicit data-plane role (`Key Vault Secrets Officer` for read/write). `az role assignment create` had a `MissingSubscription` bug on this endpoint (F15b) — use `az rest --method put` fallback | Post-Bicep: detect operator OID + KV RBAC mode; if RBAC=true, grant `Key Vault Secrets Officer` (role ID `b86a8fe4-44ce-4948-aee5-eccb2c155cd7`) via `az rest`; poll `az keyvault secret list` until non-403. Idempotent | ✅ Applied manually this session for per-tenant KV; automation TODO — operator-RBAC-bootstrap step in H3 or pre-H4 |
| F16 | Shared BFF App Service `keyVaultReferenceIdentity` set to literal `"SystemAssigned"` but only UserAssigned identity attached → all `@Microsoft.KeyVault(...)` refs silently unresolvable. Also shared UAMI has 0 data-plane RBAC on the shared KV. Two independent misconfigs, both from Bicep | (Not yet applied — planned) Grant shared UAMI `Key Vault Secrets User` on shared KV via `az rest`; PATCH kvRefIdentity to UAMI resource ID (not "SystemAssigned") via `az webapp update --set keyVaultReferenceIdentity=...`; apply to both production + staging slots per T1 rule; restart App Service | Bicep hardening TODO: (a) never emit `keyVaultReferenceIdentity='SystemAssigned'` when only UAMI attached, (b) emit role assignments for attached UAMIs on referenced KVs. T1 handler must verify + auto-remediate |
| F17 | Bicep deploy provisions App Service resource but does NOT deploy BFF code. Root URL returns default "empty App Service" page; `/healthz` returns 404. All subsequent config work (KV refs, App User, /healthz verify) tests an empty shell | Fresh-env case: detect empty App Service via root-URL response check; build+publish BFF (`dotnet publish -c Release`); zip-deploy via `az webapp deploy --type zip`; poll `/healthz` with 30-90s warm-up backoff. NFR-01 constrains publish size ≤60 MB | H9 handler exists as name in catalog but no code yet. Reference `.claude/skills/deploy-new-release/SKILL.md` (built for shared-env slot-swap, needs fresh-env variant) — verified this session with 46 MB compressed publish (PASSES NFR-01) |
| F16.5 | `az webapp update -g <rg> -n <name> --set keyVaultReferenceIdentity="<uami-resource-id>"` returns `ERROR: Operation returned an invalid status 'Bad Request'` — the CLI wrapper does not accept this property path (mirror of F15b bug pattern for a different endpoint). Blocks the F16 Part B remediation via the obvious CLI form | Use `az rest --method patch --url "https://management.azure.com/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Web/sites/{name}?api-version=2022-03-01" --body '{"properties":{"keyVaultReferenceIdentity":"<uami-resource-id>"}}'` fallback | ✅ Fallback applied this session; T1 handler should skip `az webapp update --set` and go straight to `az rest --method patch` (or file Azure CLI issue upstream) |
| F18 | Fresh RBAC-enabled SHARED KV grants NO data-plane access even to subscription Owner — SAME F15 pattern for a different vault. Shared KV `sprk-prod-kv` blocked operator from listing/reading/writing secrets during F19 investigation | Same as F15: pre-H4 bootstrap grants operator `Key Vault Secrets Officer` via `az rest` fallback. Idempotent. Extend F15 handler to ALL fresh RBAC-enabled KVs in scope (per-tenant AND shared) | ✅ Applied manually this session; automation TODO absorbed into F15 handler (widen scope to enumerate every KV in RG) |
| F19 | Bicep deploy provisions shared KV `sprk-prod-kv` but leaves it EMPTY — all 6 BFF `@Microsoft.KeyVault(...)` references point to non-existent secrets. Even after F16 fixes (RBAC + kvRefIdentity), refs cannot resolve. BFF fail-fast at boot on missing config downstream | H4-shared handler: extract keys/conn-strings from source Azure services (AI Search admin key, Cog Svc key1, SB RootManageSharedAccessKey, Storage conn string, Redis primaryKey composed with `<host>:<port>,password=<key>,ssl=True,abortConnect=False`), seed to KV under canonical secret names matching App Service KV-ref manifest. Idempotent | ✅ Applied manually this session (6 secrets seeded); H4 handler needs extending — currently H4 is scoped per-tenant KV; needs sibling H4-shared handler + canonical secret-name manifest (with F19 initial map) |
| F20 | BFF crashes at startup with exit code 134 (SIGABRT) via `Unhandled exception. System.InvalidOperationException: SpeAdmin:KeyVaultUri (or KeyVaultUri) configuration is required for SpeAdminModule.` (`SpeAdminModule.cs:45`). Bicep provisions App Service with 14 app settings incl. 6 KV refs — but NOT the app settings that hard-required IOptions modules read directly (bypass `ValidateOnStart`). BFF has ~40 `.ValidateOnStart()` modules — many will surface as F20a/b/c... in sequence | H4b-BulkAppSettings handler (companion to H4-KV-seed): read canonical app-settings template from a manifest, resolve KV refs, `az webapp config appsettings set --settings k1=v1 k2=v2 ...` (single call, single restart). Template driven from `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` § App Service settings + per-env inputs (TenantId, BFF ClientId, ContainerTypeId, Webhook signing keys, EmailProcessing signing key) | ❌ NOT YET REMEDIATED — chose NOT to serially chase 40-module chain this session; single setting `SpeAdmin__KeyVaultUri` applied revealed F20a. Full remediation deferred to design of H4b handler |
| F20a | After F20 setting applied, next fail-fast: `Unhandled exception. System.InvalidOperationException: CosmosPersistence:Endpoint is not configured. Add this setting to appsettings.json or Azure App Service configuration.` (`AiPersistenceModule.cs:56`). Progressive discovery pattern confirmed | Same as F20 — batch-set all required app settings from canonical template in one call | ❌ NOT YET REMEDIATED — evidence for F20 progressive chain; validates the "40 IOptions modules" scale claim |

### What r1 delivery still needs (roadmap)

Before r1 can claim E2E-no-human-interaction:

1. **Absorb F1-F20 into automated Step 2.5 + H4/H4b handlers + H6 solution-import handler + H9 BFF-deploy handler** (currently mostly informational)
2. **Codify Spaarke canonical region defaults**: westus2 platform + westus3 OpenAI (baked into `Model 1 Prod` profile in `pac admin create`)
3. **Parameterize `sharedOpenAiDeployments`** in `stacks/model1-shared.bicep` so skill can compute the deployment set at runtime (auto-quota compatible → full P5 progressive upgrade)
4. **Auto-registration retry-verify** for all `Microsoft.*` providers
5. **Auto-support-ticket flow** for cases where auto-grant path doesn't exist (advanced, gated on operator having Support Plan)
6. **Introduce `Required Applications` manifest** on H6 solution-import handler (F13): config-driven list of AppSource apps that MUST be pre-installed on any Spaarke target env before SpaarkeMaster import. Initial list: `msft_PowerBI_Anchor`. Pre-import intersect + auto-install via `pac application install` loop.
7. **Introduce `Org Settings Contract`** on H6 solution-import handler (F14): config-driven map of `settingName → minValue` that MUST be applied to any Spaarke target env before SpaarkeMaster import. Initial map: `maxuploadfilesize: 25_600_000`. Pre-import diff + auto-apply via `pac org update-settings`. Idempotent, single-call, ~2s per setting.
8. **Add nightly smoke-install job** for `Build-SpaarkeMaster.ps1` (F12 forcing-function): re-export managed .zip → extract solution.xml → CI asserts `MissingDependency solution="Active"` count == 0. Catches regressions in the leaky-export fix before they reach fresh-env installs.
9. **Operator-RBAC-bootstrap step** (F15): idempotent pre-H4 grant of `Key Vault Secrets Officer` to operator on every RBAC-enabled KV, via `az rest` (F15b bypass). Uses `az ad signed-in-user show` for OID auto-detect. Silent success on re-run.
10. **Bicep hardening for kvRefIdentity + UAMI-KV RBAC** (F16): (a) reject `keyVaultReferenceIdentity='SystemAssigned'` combined with UserAssigned-only identity in the Bicep template; (b) auto-emit role assignments for attached UAMIs on referenced KVs. Backstop: T1 handler verifies + auto-remediates any drift post-deploy.
11. **Fresh-env BFF deploy handler** (F17): H9 currently exists as a catalog name only. Needs code that (a) detects empty-App-Service state, (b) builds + zip-deploys BFF, (c) polls `/healthz` with warm-up backoff, (d) sequences AFTER F16 remediation so BFF starts in configured state (not degraded). **This session verified: 46 MB compressed publish passes NFR-01 60 MB ceiling; `az webapp deploy --type zip` uploads cleanly but Site Startup Probe fails when config chain (F20) unresolved.**
12. **H4-shared handler + canonical secret manifest** (F19): sibling to H4 (per-tenant); H4-shared extracts keys from source Azure services (AI Search admin key, Cog Svc key1s, SB RootManageSharedAccessKey, Storage conn string, Redis composed conn string) and seeds to shared KV under canonical secret names. Initial 6-secret manifest: `AiSearch--AdminKey`, `DocumentIntelligence-ApiKey`, `AzureOpenAI-ApiKey`, `servicebus-connection-string`, `storage-connection-string`, `redis-connection-string` (must MATCH the App Service `@Microsoft.KeyVault(SecretName=...)` refs). **📝 Handler POML designed 2026-08-24 SESSION 3: [`projects/customer-provisioning-orchestration-r1/tasks/200-implement-h4-shared-kv-source-extraction-handler.poml`](../../../projects/customer-provisioning-orchestration-r1/tasks/200-implement-h4-shared-kv-source-extraction-handler.poml). Extends task 084 manifest schema with `source: { type, service-ref }` field. Includes IArmKeyVaultRefProbe post-condition (uses F16-remediated kvRefIdentity). Bicep hardening implied: L2 UAMI needs 5 new RBAC assignments on source services (`Cognitive Services User`, `Search Service Contributor`, `Azure Service Bus Data Owner`, `Storage Account Contributor`, `Redis Cache Contributor`).**
13. **H4b-BulkAppSettings handler** (F20/F20a): CRITICAL NEW HANDLER. Reads canonical BFF app-settings template (~40 IOptions modules × ~2-4 settings each ≈ 80-160 app settings) + resolves KV refs + per-env inputs (TenantId, BFF ClientId, ContainerTypeId, WebhookSigningKeys, EmailProcessing WebhookSigningKey), calls `az webapp config appsettings set --settings k1=v1 k2=v2 ...` in single batch to trigger ONE restart cycle. Manifest source: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` § App Service settings. **This handler is the difference between "BFF App Service exists" and "BFF actually boots" — without it, F20 chain progressively reveals ~40 missing configs.** **📝 Handler POML designed 2026-08-24 SESSION 3: [`projects/customer-provisioning-orchestration-r1/tasks/201-implement-h4b-bulk-appsettings-handler.poml`](../../../projects/customer-provisioning-orchestration-r1/tasks/201-implement-h4b-bulk-appsettings-handler.poml). Introduces NEW canonical manifest at `scripts/canonical-app-settings/manifest.yaml` (sibling to task 084 secret-catalog). Diff-first idempotency preserves operator overrides. IHealthzProbe polls `/healthz` with 8-min backoff + parses container docker-logs on failure to extract fail-fast module name for actionable diagnostic. Sequencing: H4-shared || H4-per-tenant → H4b → H9 → BFF boots configured.**
14. **F16 Bicep hardening (extend)**: (a) never emit `keyVaultReferenceIdentity='SystemAssigned'` when only UserAssigned attached, (b) auto-emit role assignments for attached UAMIs on referenced KVs — **AND** (c) via F16.5 discovery: T1 handler skips `az webapp update --set keyVaultReferenceIdentity=...` (returns Bad Request); goes straight to `az rest --method patch` on the site resource with `{"properties":{"keyVaultReferenceIdentity":"..."}}` body.

### The Customer Deployment Web App (natural evolution)

Owner-directed follow-on project: replace the operator-invoked skill with a self-service web UI. Prospect/ops user fills a form; the L3 skill (fully automated per above) executes end-to-end; SSE stream provides real-time progress.

Prerequisites:
- All F1-F9 absorbed into fully-automated Step 2.5
- L2 control-plane `/api/runs` API surfaced via BFF
- All handlers H0-H14 live-validated (in-progress; see current-task.md for status)

Filing: proposed as `projects/customer-deployment-webapp-r1` (skeleton to be created after r1 completes).

---

*Fresh-Sub Automation Gaps section added 2026-08-22 during Model 1 Prod first-live stand-up. Preserves the discovery arc for future operators + subsequent r1 automation absorption.*
