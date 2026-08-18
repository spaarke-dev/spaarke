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
| L2 API base (dev) | `https://spaarke-provisioning-dev.azurewebsites.net` |
| L2 API base (prod) | `https://spaarke-provisioning-prod.azurewebsites.net` |
| L2 REST surface | `POST /api/runs`, `GET /api/runs/{id}`, `POST /api/runs/{id}/resume`, `POST /api/runs/{id}/clear-quarantine` |
| L2 audience (token) | `api://spaarke-provisioning-controlplane-{env}` |
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
- **MUST** call L2 REST API with `Authorization: Bearer <token>` where token is acquired via `az account get-access-token --resource api://spaarke-provisioning-controlplane-{env}`
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
  --resource "api://spaarke-provisioning-controlplane-$env" `
  --query accessToken -o tsv

# Health check L2 (unauth endpoint)
$l2Base = if ($env -eq "prod") { "https://spaarke-provisioning-prod.azurewebsites.net" } `
          else { "https://spaarke-provisioning-dev.azurewebsites.net" }
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
  [PASS] L2 API reachable (dev): https://spaarke-provisioning-dev.azurewebsites.net
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
  L2 API:        https://spaarke-provisioning-dev.azurewebsites.net

Proceed to preflight (H0)? (yes/no)
```

Wait for "yes" (bare "y" is insufficient at every gate in this skill — spec §4.3a.4).

---

### Step 2: Preflight (invokes L2 H0 handler)

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
         --resource "api://spaarke-provisioning-controlplane-$env" `
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
| 5 | AAD bearer for L2 | `az account get-access-token --resource api://spaarke-provisioning-controlplane-{env}` | Interactive `az login` first |
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

**Role**: `Operator` app-role on the control-plane app-reg `api://spaarke-provisioning-controlplane-{env}`. Assigned via:

```
az ad app show --id api://spaarke-provisioning-controlplane-dev --query "id"
# → objectId of the app-reg's SP
az rest --method POST --uri "https://graph.microsoft.com/v1.0/servicePrincipals/{spObjId}/appRoleAssignments" `
  --body '{ "principalId":"{operatorObjId}", "resourceId":"{spObjId}", "appRoleId":"{operatorRoleGuid}" }'
```

**Token**: acquired once per run, refreshed on 401:

```powershell
$token = az account get-access-token `
  --resource "api://spaarke-provisioning-controlplane-{env}" `
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
| Step 0c returns 403 on role probe | Operator not granted `Operator` app-role on `api://spaarke-provisioning-controlplane-{env}` | Ask a control-plane admin to run the `az rest` app-role assignment (see Auth Flow) |
| Step 0c token acquisition fails: `AADSTS500011` | Resource URI wrong OR app-reg not exposed in operator's tenant | Verify `az ad app show --id api://spaarke-provisioning-controlplane-{env}` returns a value; if not, wrong env |
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
