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
| Handler catalog | 15 handlers: H0 preflight → H0.5 consent-callback → H1..H14 provisioning steps (see [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §H0–H14) |
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
- **MUST NEVER delete** `Dataverse-ClientSecret` or `BFF-API-ClientSecret` (BINDING per root CLAUDE.md §10 + r3 handoff — these secrets are still consumed by OBO flow)

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

#### 0b. Operator AAD identity

```powershell
az account show --query "{name:name, tenantId:tenantId, user:user.name}" -o json
az ad signed-in-user show --query "{oid:id, upn:userPrincipalName}" -o json
```

Assertions:
- `tenantId` MUST equal the Spaarke tenant ID (`a221a95e-6fa6-4f6b-9a3c-19a1c1a56d7e` — verify from environment; fail-fast if mismatched)
- `user.name` MUST be a real UPN (not a service-principal ObjectId)
- If the returned identity is a service principal, HARD STOP with message: "L3 skill requires operator's own AAD identity per NFR-11. Run `az login` interactively. Refusing to proceed under SP auth."

#### 0c. L2 API reachability + Operator role

```powershell
# Acquire token — env is one of {dev, prod}
$env = "dev"  # or prod (from intake or arg)
$token = az account get-access-token `
  --resource "api://spaarke.com/provisioning-controlplane-$env" `
  --query accessToken -o tsv

# Health check L2 (unauth endpoint)
$l2Base = if ($env -eq "prod") { "https://spaarke-provisioning-controlplane-prod.azurewebsites.net" } `
          else { "https://spaarke-provisioning-controlplane-dev.azurewebsites.net" }
curl -sf "$l2Base/healthz"  # expect 200

# Role probe — call a known Operator-only endpoint with a well-formed but obviously-invalid payload; expect 400 (validation error) NOT 403 (forbidden)
curl -sS -o /dev/null -w "%{http_code}" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{"customerId":"__role-probe__","tenancyModel":"Model1Shared","profile":"dev","tenantId":"__probe__"}' `
  "$l2Base/api/runs"
# Expect 400 (validation) — proves Operator role is granted. If 403 → operator does NOT have Operator role; HARD STOP with grant instructions.
```

#### 0d. Dataverse MCP status (optional but strongly recommended)

Attempt an MCP ping (`mcp__dataverse__describe` against a known small table). If MCP is disconnected:

```
⚠ Dataverse MCP is not connected.
  Impact: registry updates on run completion will use the fallback matrix
          (pac data / raw Web API PS) — slower but functional.
  Continue anyway? (yes/no)
```

MCP status is NOT a hard stop — the fallback matrix handles disconnect (see Fallback Matrix section, added by task 076).

#### 0e. Working directory + git state

```powershell
git rev-parse --show-toplevel   # verify inside a repo (operator's working tree)
git status --porcelain          # note uncommitted changes (informational; not blocking)
```

Runs create `runs/{runId}.md` in the operator's cwd. If cwd is not a git repo, warn: "handoff report will be written to cwd but won't be checkpointed to git — consider running from repo root."

#### 0f. Report + gate

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

### Step 1: Interactive Intake

Collect the 4 inputs the L2 REST API requires. If the operator passed `{customerId}` as a slash-command arg, pre-fill it. Otherwise ask.

#### 1a. `customerId` (required)

- Format: `[a-z][a-z0-9-]{2,31}` (kebab-case, 3-32 chars, starts alpha)
- Uniqueness: probe `GET /api/runs?customerId={id}` — if any run exists, present the operator with the existing run history and confirm: "customerId `{id}` has {N} prior runs. Continue as an UPGRADE run? (yes/no)"
- If reused → this is an upgrade-mode run (per FR-34 §14A upgrade model); the operator MUST confirm intent
- If new → this is a fresh-provisioning run

#### 1b. `tenantId` (required per I1 invariant — NEVER default)

- Format: RFC 4122 GUID
- The customer's Entra tenant ID (Model 2: their tenant; Model 1: Spaarke's shared tenant)
- Do NOT default; do NOT fall back to `az account show` — the operator MUST supply this explicitly. This enforces the §4D I1 tenant-isolation invariant (FR-28).

#### 1c. `tenancyModel` (required)

Choice:
- `Model1Shared` — shared trial / SMB tier (multi-tenant BFF, shared Dataverse, per-customer container in SPE)
- `Model2Dedicated` — dedicated Azure subscription + dedicated Dataverse env + admin-consent flow required

Explain the trade-off to the operator if they ask.

#### 1d. `profile` (required)

Choice: `dev` / `demo` / `prod` — determines which L2 API base + which Bicep parameter file is used.

#### 1e. Show intake summary

```
INTAKE SUMMARY
  customerId:    trial-acme-2026-08-18
  tenantId:      12345678-...-...-...  (customer tenant)
  tenancyModel:  Model1Shared
  profile:       dev
  L2 API:        https://spaarke-provisioning-controlplane-dev.azurewebsites.net

Proceed to preflight (H0)? (yes/no)
```

Wait for "yes" (bare "y" is insufficient at every gate in this skill — spec §4.3a.4).

---

### Step 2: Preflight (invokes L2 H0 handler)

> **BEFORE this step**, if the target Azure subscription was created within the last 90 days (i.e. "fresh sub"), invoke **Step 2.5 (Fresh-Sub Deployment Feasibility Check)** first. Fresh subs have region/quota/model gotchas that L2's H0 handler does NOT currently check for; skipping Step 2.5 leads to preflight failure loops that the operator cannot escape without editing Bicep. See "Fresh-Sub Automation Gaps" section at end of this file for the full evidence base (customer-provisioning-orchestration-r1 lessons learned 2026-08-22).

Preflight is idempotent + fast (<30s). It:
- Validates quota (Azure OpenAI regional TPM per NFR-12; App Service tier; SPE container-type headroom)
- Runs DNS pre-check for reserved sub-domains
- Verifies customer's tenant is reachable + admin consent status (Model 2 only)
- Confirms operator's grants against target subscription (Model 2 only)

Invocation:

```powershell
$body = @{
  customerId    = $customerId
  tenantId      = $tenantId
  tenancyModel  = $tenancyModel
  profile       = $profile
  mode          = "preflight"    # H0-only run; does NOT enqueue H1-H14
} | ConvertTo-Json

$response = Invoke-RestMethod `
  -Uri "$l2Base/api/runs" `
  -Method POST `
  -Headers @{ Authorization = "Bearer $token" } `
  -Body $body -ContentType "application/json"

# response: { runId: "...", status: "Accepted" }
$runId = $response.runId
```

L2 returns 202 Accepted within 100ms (FR-22 R20). Poll `GET /api/runs/{runId}` at 5s intervals until H0 reaches `Succeeded` or `Failed`. Cap total wait at 60s (H0 is fast); if exceeded, escalate.

Present H0 outcome:

```
PREFLIGHT (H0) RESULT
  Duration: 8.2s
  [PASS] Azure OpenAI TPM headroom OK (projected 187/2000 sum-across-models)
  [PASS] App Service plan tier available in westus2
  [PASS] SPE container-type headroom OK (7,442 of 10,000 remaining)
  [PASS] DNS pre-check: trial-acme-2026-08-18.spaarke.com not reserved
  [PASS] Customer tenant reachable (Model 1 shared)
  [PASS] Estimated cost: $412/mo (within $430 Model 1 marginal envelope)
  [PASS] Estimated duration: 42 min (H1-H14, no lead-time gates)

Preflight passed. Proceed to Step 3 (confirmation gate)? (yes/no)
```

If H0 FAILS, present the failure + escalation instructions (per §4C 4-class taxonomy). Do NOT proceed to Step 3.

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

  Handlers to execute (11 for Model1Shared / 15 for Model2Dedicated):
    H0.5  consent-callback (Model 2 only — skipping for Model 1)
    H1    resource-group provisioning
    H2a   Bicep infra apply (30-min timeout)
    H2b   AI Search index deploy (7 canonical indexes)
    H3    KV secret bootstrap
    H4    canonical secret population
    H5    Dataverse environment creation (20-min timeout for Model 2)
    H6    Dataverse solutions import (8 solutions, dependency-ordered)
    H7    env-var writes to customer env
    H8    SPE container-type creation (24h replication, gate H8.a re-verifies)
    H9    BFF deploy to customer stamp (blue-green via staging slot)
    H10   Dataverse App User creation (UAMI-based)
    H11   demo user provisioning (Model 1 only for trial users)
    H12a  AI seed chain (playbooks + embeddings)
    H12b  playbook consumers seed
    H12c  agents seed
    H13   acceptance gate (all traps clear + invariants pass + cost envelope)
    H14   Exchange ApplicationAccessPolicy verification (T4)

  Estimated wall-clock: 42 min (no lead-time gates surfaced by H0)
  Estimated cost impact: +$412/mo (Model 1 marginal, within envelope)

  Manual gates you MAY encounter mid-run:
    - Model 2 admin consent URL (H0.5) — customer admin clicks
    - Azure quota bump (if H1 hits soft cap) — operator opens support ticket
    - SPE container replication wait (H8) — 24h; H8.a resumes automatically

  DESTRUCTIVE OPERATIONS: none in fresh-provisioning mode. Upgrade mode may
  overwrite Bicep-managed resources with drift; if this is an upgrade run,
  H2a's what-if diff will be presented separately before applying.

To proceed with this run, type the exact phrase:

    proceed with provisioning

(a bare "y" or "yes" is NOT accepted at this gate — spec §4.3a.4)
```

Wait for the literal string `proceed with provisioning`. Anything else — including "y", "yes", "go", "ok" — prompts a re-ask with the same gate. This is by design per NFR-11 auditability (the operator's explicit phrase is captured in the run's audit trail).

---

### Step 4: Execute Loop — enqueue → poll → advance

Once "proceed with provisioning" received, transition the run from `Preflight-Only` to `Executing`:

```powershell
Invoke-RestMethod `
  -Uri "$l2Base/api/runs/$runId/resume" `
  -Method POST `
  -Headers @{ Authorization = "Bearer $token" } `
  -Body (@{ mode = "execute" } | ConvertTo-Json) -ContentType "application/json"
```

L2 begins enqueuing H0.5..H14 per its state machine + reconciler (FR-22).

#### 4a. Poll loop

Poll `GET /api/runs/{runId}` at **10s intervals** (per H1 15-30s guidance; 10s is slightly more responsive without overwhelming L2). Auto-refresh the token when a 401 appears (see Fallback Matrix).

Track TodoWrite entries for each handler as it enters/exits `Running`:
- `Handler H2a (bicep-apply) — Running`
- `Handler H2a (bicep-apply) — Succeeded (28m 14s)`
- `Handler H6 (dv-solutions) — Running`
- ...

Present progress to the operator every ~5 completed handlers OR on any state transition (`WaitingOnGate`, `Failed`, `Quarantined`).

#### 4b. Handle each terminal state

The run's `status` field transitions through:

| Status | Meaning | Skill action |
|---|---|---|
| `Accepted` | 202 returned; work not started | Poll |
| `Executing` | Handlers running | Poll; update TodoWrite |
| `WaitingOnGate` | Handler paused pending external condition | See Step 5 (manual gate handling) |
| `Succeeded` | All handlers completed + H13 acceptance passed | See Step 6 (completion handoff) |
| `Failed` | Handler failed with `Retryable*` or `Resumable` class | Present failure + `POST /api/runs/{id}/resume` option to operator |
| `Quarantined` | Handler failed with `QuarantineRequired` class | HARD STOP; require `POST /api/runs/{id}/clear-quarantine` with reason |
| `Drifted` | H13 detected `Successful-but-drifted` state | Present drift report + `resumeFromPhase` option |

Do NOT auto-retry `Failed` runs. Auto-retry hides operator-actionable diagnostics. Ask.

---

### Step 5: Manual Gate Handling

Some handlers reach `WaitingOnGate` because they require operator-visible action:

#### 5a. H0.5 Model 2 admin consent (Model 2 only)

```
🔔 MANUAL GATE: Customer admin consent required (Model 2)

  Handler: H0.5 consent-callback
  Reason:  The multi-tenant BFF app-reg needs admin consent on the customer's
           Entra tenant before H5 can create a Dataverse Application User.

  ACTION FOR CUSTOMER ADMIN (send this URL to the customer):
    https://login.microsoftonline.com/{customerTenantId}/adminconsent
      ?client_id={bff-multi-tenant-app-id}
      &redirect_uri=https://spaarke-bff-prod.azurewebsites.net/api/onboarding/consent-callback
      &state={runId}

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
    4. Return here and type 'resume' to retry H1

  The skill will hold at this gate until you type 'resume' or 'abandon'.
```

#### 5c. H8 SPE 24h replication wait

```
🔔 MANUAL GATE: SPE container-type replication in progress (H8)

  Handler: H8 spe-container-create
  Reason:  Container-type created successfully but Microsoft-side replication
           takes ~24h before H8.a can verify or H9 can bind BFF to the container.

  ACTION: none required — this is expected. The skill will exit and re-invoke
          H8.a automatically 25 hours from now.

  Alternatively, keep the skill running and it will poll every hour.

  Estimated resume time: {timestamp + 25h}
```

#### 5d. Generic pattern for any other `WaitingOnGate`

```
🔔 MANUAL GATE: {gate name} ({handler name})

  Reason: {gate reason from L2 response}
  Action: {gate.instructions from L2 response}

  Type 'resume' when the action is complete (L2 will re-verify).
  Type 'abandon' to quarantine the run.
```

**IMPORTANT**: NEVER auto-advance past a gate by trusting the operator's assertion. Always call `POST /api/runs/{id}/resume` and let L2 re-verify the underlying condition (Dataverse query, Graph query, Azure resource state). If verification fails, the run stays at `WaitingOnGate`.

---

### Step 6: Completion Handoff

When the run reaches `Succeeded` (H13 acceptance passed):

#### 6a. Update `sprk_dataverseenvironment` registry

Via Dataverse MCP (primary) OR fallback (see Fallback Matrix):

```
mcp__dataverse__update_record(
  entityName: "sprk_dataverseenvironment",
  recordId: {resolved from customerId},
  fields: {
    sprk_provisionedon: "{completedAt ISO timestamp}",
    sprk_currentrunid: null,             // clear the concurrency lock
    sprk_bffversion: "{deployedBffVersion}",
    sprk_solutionversion: "{deployedSolutionVersion}",
    sprk_tenantid: "{tenantId}",
    sprk_setupstatus: 200000004          // "Ready" per option-set integer
  }
)
```

If MCP is disconnected, the fallback matrix triggers `pac data update` OR raw Web API PATCH. Both work with the operator's `az` token (no re-auth needed).

#### 6b. Write handoff report

`runs/{runId}.md` in the operator's cwd. Structure:

```markdown
# Provisioning Run {runId}

- **Customer**: {customerId}
- **Tenant**: {tenantId}
- **Tenancy Model**: {tenancyModel}
- **Profile**: {profile}
- **Started**: {startedAt}
- **Completed**: {completedAt}
- **Wall-clock duration**: {duration}
- **Status**: Succeeded / Failed / Quarantined / Drifted
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

#### 6c. Final summary to operator

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

       # Write (Step 6 registry update)
       pac data update --entity sprk_dataverseenvironment `
         --id {envRecordId} `
         --data '{"sprk_provisionedon":"{timestamp}","sprk_setupstatus":200000004,"sprk_currentrunid":null}'

  4. Fallback B (if pac unavailable OR command shape not supported): raw Web API PS
       $dvUrl = "https://{customerEnv}.crm.dynamics.com"
       $dvToken = az account get-access-token --resource $dvUrl --query accessToken -o tsv
       $body = @{
         sprk_provisionedon = "{timestamp}"
         sprk_setupstatus   = 200000004
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
az ad app show --id api://spaarke-provisioning-controlplane-dev --query "id"
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
- **NEVER delete** `Dataverse-ClientSecret` or `BFF-API-ClientSecret` — they're still consumed by OBO. This is BINDING regardless of what the run appears to require.

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
| Handoff report not written on failure | Skill treated failure as "no report needed" | Report is written on ALL terminal states (Succeeded, Failed, Quarantined, Drifted). Failure reports capture the failure mode + diagnostic + resumption instructions. |
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
12. **H4-shared handler + canonical secret manifest** (F19): sibling to H4 (per-tenant); H4-shared extracts keys from source Azure services (AI Search admin key, Cog Svc key1s, SB RootManageSharedAccessKey, Storage conn string, Redis composed conn string) and seeds to shared KV under canonical secret names. Initial 6-secret manifest: `AiSearch--AdminKey`, `DocumentIntelligence-ApiKey`, `AzureOpenAI-ApiKey`, `servicebus-connection-string`, `storage-connection-string`, `redis-connection-string` (must MATCH the App Service `@Microsoft.KeyVault(SecretName=...)` refs).
13. **H4b-BulkAppSettings handler** (F20/F20a): CRITICAL NEW HANDLER. Reads canonical BFF app-settings template (~40 IOptions modules × ~2-4 settings each ≈ 80-160 app settings) + resolves KV refs + per-env inputs (TenantId, BFF ClientId, ContainerTypeId, WebhookSigningKeys, EmailProcessing WebhookSigningKey), calls `az webapp config appsettings set --settings k1=v1 k2=v2 ...` in single batch to trigger ONE restart cycle. Manifest source: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` § App Service settings. **This handler is the difference between "BFF App Service exists" and "BFF actually boots" — without it, F20 chain progressively reveals ~40 missing configs.**
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
