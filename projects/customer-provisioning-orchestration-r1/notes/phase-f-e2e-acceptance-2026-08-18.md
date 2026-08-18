# Phase F E2E Acceptance Report — Model 2 Dedicated (LIVE / IN PROGRESS)

> **Status**: LIVE — invocation started by main-session on 2026-08-18 per owner authorization; owner in-loop for 2 gates (Step 3 confirmation phrase + Step 5 H0.5 admin consent URL).
> **Running-log convention**: main-session appends findings as it runs; owner can inline feedback anywhere with `>` blockquotes marked `[OWNER]`. Table cells fill in as data arrives. `{...}` placeholders = not yet observed.
> **Companion**: [`notes/phase-f-operator-runbook.md`](phase-f-operator-runbook.md) (step-by-step wrapper) · [`notes/phase-f-verification-harness.md`](phase-f-verification-harness.md) (verification commands per trap/invariant/naming/cost).
> **Path A exception** (POML deviation): Model 2 dedicated is now the primary acceptance path (was Model 1 per original POML mandate); rationale = dedicated stamp exercises fresh App Service + OpenAI + AI Search architecture. Formalized in `089-phase-f-e2e-acceptance.poml` amendment 2026-08-18.

---

## Running log (main-session appends inline as events occur)

_Latest at bottom._

- `2026-08-18 [prep]` Running notes file created from skeleton at `notes/phase-f-e2e-acceptance-2026-08-18.md`. About to run Step 1 prereqs (read-only, no mutations).
- `2026-08-18 [step-0a-b]` Prereqs OK: identity `ralph.schroeder@spaarke.com` (real UPN, not SP); tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2` (Spaarke); subscription `484bc857-3802-427f-9ea5-ca47b43db0f0` (Spaarke Development Environment); pwsh 7.6.3, az-cli 2.77.0, pac CLI installed, git 2.51.0 — all meet minimums.
- `2026-08-18 [step-0c BLOCKER]` **L2 control-plane App Service does NOT exist in dev subscription.** Discovery:
    - `curl https://spaarke-provisioning-dev.azurewebsites.net/healthz` → HTTP 000 (unreachable / DNS resolution failure)
    - `az webapp list` (all RGs in dev sub) → only `spaarke-bff-dev` (Running) + `spaarke-bff-prod` (Stopped). No L2 App Service anywhere.
    - `az deployment group list` in rg-spaarke-dev filtered for `platform-controlplane` → empty. `platform-controlplane.bicep` (task 033) has never been applied to dev.
    - Token acquired successfully for `api://spaarke-provisioning-controlplane-dev` audience — proves the AAD app-reg exists, but that's registration, not deployment.
- `2026-08-18 [scope-discovery]` Project completed all L2 CODE work (tasks 033/036/037/038/039/057/058-062) but the L2 App Service was never PROVISIONED + DEPLOYED to dev. Task 034 (end-to-end Bicep integration test) was DRY-RUN only per its title ("Bicep deploy dry-run"). No task explicitly owned "apply L2 Bicep to dev + deploy L2 code + verify /healthz on L2". This is a prerequisite gap for Phase F E2E acceptance.
- `2026-08-18 [decision-required]` **PAUSED for owner decision on path forward** — Phase F acceptance cannot proceed without either (A) deploying L2 first as new sub-project scope, (B) deferring Phase F entirely, or (C) running L2 locally against localhost. See main-session's blocker report + options table.
- `2026-08-18 [owner-decision-1]` Owner authorized full sprint: deploy L2 (L2-0 through L2-7) then run task 089 in this session.
- `2026-08-18 [prereq-check]` Deeper prereq investigation surfaced ADDITIONAL gaps beyond just "L2 App Service missing":
    - **L2 AAD app-reg does NOT exist**: `AADSTS500011 — The resource principal named api://spaarke-provisioning-controlplane-dev was not found in the tenant`. Must be CREATED as L2-0 prereq (with Operator + Reader app-roles + Operator assignment to owner).
    - **`Dataverse-ClientSecret` KV secret does NOT exist** in `spaarke-spekvcert` (r3 task 060 dropped the Dataverse S2S app-reg; MI-based auth now). Bicep default `dataverseClientSecretName = 'Dataverse-ClientSecret'` will fail to resolve — need param override or Bicep update.
    - **SB secret naming mismatch**: Bicep default `servicebus-connection-string` (lowercase-hyphens) vs actual KV secret `ServiceBus-ConnectionString` (PascalCase-hyphens). Need `serviceBusKeyVaultSecretName` param override.
    - ServiceBus namespace `spaarke-servicebus-dev` in RG `SharePointEmbedded` exists ✅ (unusual RG naming but functional).
    - Earlier "token acquired: 733 chars" for L2 audience was a cached/fallback token; re-running now correctly shows AADSTS500011 (app-reg genuinely missing).
- `2026-08-18 [owner-decision-2]` Owner authorized continued sprint: create L2 app-reg + fix Bicep param overrides in-session. Estimated 6-12 hours realistic.
- `2026-08-18 [L2-0 START]` Beginning L2 app-reg creation. Will pause + escalate if permission errors surface (creating app-regs may require Entra AD admin role).
- `2026-08-18 [L2-0 DONE]` L2 app-reg created + configured end-to-end. Details:
    - App-reg: `spaarke-provisioning-controlplane-dev` — appId `70ba7b19-8969-47e5-a508-efe621dea1a4`, objectId `ff3babca-4c33-4966-9880-a1121a0734c8`
    - SP objectId: `583b34f7-6992-4a93-83d1-99296531325c`
    - Identifier URI: `api://spaarke.com/provisioning-controlplane-dev` (tenant policy required verified domain; original `api://spaarke-provisioning-controlplane-dev` rejected)
    - App roles: `Operator` (id `6433ca3e-739d-4d72-81c8-5feb4c4fe73b`) + `Reader` (id `ae046316-d58e-4160-b2f3-98d503c91695`)
    - Azure CLI SP (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`) created in Spaarke tenant + granted user_impersonation delegated consent on L2 API
    - Operator role assigned to ralph.schroeder@spaarke.com (oid `c74ac1af-ff3b-46fb-83e7-3063616e959c`)
    - Token acquisition verified: aud=`api://spaarke.com/provisioning-controlplane-dev`, roles=`['Operator']`, upn=`ralph.schroeder@spaarke.com`
    - **Governance-note**: jwtAudience in Bicep var `api://spaarke-provisioning-controlplane-${environmentName}` does NOT match actual `api://spaarke.com/provisioning-controlplane-dev`. Override needed post-Bicep via App Service settings (`AzureAd__Audience` + `JwtBearer__Audience`).
- `2026-08-18 [L2-1 START]` Bicep apply — platform-controlplane.bicep.
- `2026-08-18 [L2-1 fix]` First apply failed on KV diagnosticSettings deprecated `retentionPolicy` field (`BadRequest — Diagnostic settings does not support retention for new diagnostic settings`). Fixed `infrastructure/bicep/modules/key-vault.bicep` — removed retentionPolicy blocks in favor of workspace-level retention (already set to 180d in monitoring.bicep). Small Bicep code change; uncommitted.
- `2026-08-18 [L2-1 DONE]` Retry succeeded. rg-spaarke-platform-dev + 7 resources created (UAMI, App Service Plan PremiumV3, Log Analytics, Cosmos DB, App Insights, KV, App Service + staging slot). App Service Running; /healthz returns 404 (no code yet, expected). Cost meter running: ~$110-120/mo baseline for L2 infra.
- `2026-08-18 [L2-1.5 BLOCKER]` KV seeding blocked by RBAC. My user (ralph.schroeder) has Contributor but LACKS User Access Administrator on the subscription → cannot create role assignments; also cannot even LIST role assignments (Microsoft.Authorization permission missing; error surfaces as misleading `MissingSubscription`). Bicep granted UAMI Secrets User (read) at deploy time but did NOT grant my user any KV rights.
- `2026-08-18 [PAUSED — awaiting owner RBAC grant]` Owner will run:
```
az role assignment create --role "Key Vault Secrets Officer" --assignee c74ac1af-ff3b-46fb-83e7-3063616e959c --scope /subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-platform-dev/providers/Microsoft.KeyVault/vaults/sprk-controlplane-dev-kv
```
When the grant lands, main-session verifies via `az keyvault secret list` and resumes L2-1.5 → L2-2 → L2-3 → L2-5/6/7.

---

## Metadata

| Field | Value |
|---|---|
| `customerId` | `{trial-2026-08-18}` <!-- kebab-case per intake format --> |
| `tenantId` | `{customer Entra tenant GUID supplied at intake}` |
| `tenancyModel` | `{Model2Dedicated}` <!-- primary path per Path A exception; note if Model1Shared discretionary run also performed --> |
| `profile` | `{trial}` |
| Run start (UTC) | `{ISO 8601 timestamp}` |
| Run end (UTC) | `{ISO 8601 timestamp}` |
| Wall-clock duration | `{Nh Nm}` <!-- compare against NFR-03 ≤1h target, excluding lead-time gates --> |
| L2 run URL | `{https://spaarke-provisioning-dev.azurewebsites.net/api/runs/{runId}}` |
| Handoff report (skill-native) | `{path to runs/{runId}.md written by the skill itself}` |

---

## Setup Status Verdict

<!-- Query sprk_dataverseenvironment for this customer; paste the raw field value -->

| Field | Value |
|---|---|
| `sprk_provisioning_setupstatus` | `{Ready / Failed / Quarantined / other}` |
| Verdict | `{PASS — reached Ready / FAIL — did not reach Ready, see Deviations}` |

---

## Per-Handler Outcomes (H0–H14, Model 2 = 15+ handlers incl. H0.5)

<!-- Fill from the skill's own handoff report (runs/{runId}.md) — this table should mirror it closely,
     with the addition of a Notes column calling out anything Phase-F-acceptance-specific. -->

| # | Handler | Status | Duration | Notes |
|---|---|---|---|---|
| 1 | H0 preflight | `{Succeeded/Failed}` | `{duration}` | `{quota/DNS/reachability notes}` |
| 2 | H0.5 consent-callback | `{Succeeded/Failed/N-A}` | `{duration}` | `{Model 2 REQUIRED — admin consent URL clicked at {timestamp}}` |
| 3 | H1 resource-group provisioning | `{}` | `{}` | `{}` |
| 4 | H2a Bicep infra apply | `{}` | `{}` | `{dedicated stamp: new UAMI/KV/Cosmos/Storage/App Service Plan/AI Search/OpenAI}` |
| 5 | H2b AI Search index deploy | `{}` | `{}` | `{7 canonical indexes on DEDICATED AI Search service}` |
| 6 | H3 Entra grants | `{}` | `{}` | `{14 grants}` |
| 7 | H4 KV secret bootstrap | `{}` | `{}` | `{T1 + T5 owner}` |
| 8 | H5 Dataverse environment creation | `{}` | `{}` | `{}` |
| 9 | H6 Dataverse solutions import | `{}` | `{}` | `{8 solutions, dependency-ordered}` |
| 10 | H7 env-var writes | `{}` | `{}` | `{points at DEDICATED OpenAI/AI Search/App Insights}` |
| 11 | H8 SPE container-type creation | `{}` | `{}` | `{24h replication gate — may show WaitingOnGate}` |
| 12 | H9 BFF deploy | `{}` | `{}` | `{blue-green slot swap}` |
| 13 | H10 Dataverse App User + Graph parity | `{}` | `{}` | `{T2 + T3 owner}` |
| 14 | H11 demo user provisioning | `{}` | `{}` | `{Model 1 only — likely N/A for Model 2; confirm}` |
| 15 | H12a AI seed chain | `{}` | `{}` | `{playbooks + embeddings}` |
| 16 | H12b playbook consumers seed | `{}` | `{}` | `{}` |
| 17 | H12c agents/runtime refs seed | `{}` | `{}` | `{sprk_aimodeldeployment → DEDICATED OpenAI deployment}` |
| 18 | H13 acceptance gate | `{}` | `{}` | `{6/6 traps clear, 5/5 invariants pass — summarized below}` |
| 19 | H14 Exchange ApplicationAccessPolicy | `{}` | `{}` | `{T4 owner}` |

---

## Per-Trap Verified (T1–T6)

<!-- One row per trap. Evidence link = path to a saved command-output log, or inline paste of the key output line. -->

| Trap | Description | Verdict | Evidence |
|---|---|---|---|
| T1 | `keyVaultReferenceIdentity` == UAMI (both slots or UAMI-spans-both) | `{PASS/FAIL}` | `{az webapp show output / log path}` |
| T2 | Dataverse App User exists for MI (systemusers count = 1) | `{PASS/FAIL}` | `{pac data query output / log path}` |
| T3 | UAMI Graph app-role parity (14/14 `GraphAppRoles.cs` roles present) | `{PASS/FAIL}` | `{az rest appRoleAssignments output / log path}` |
| T4 | Exchange ApplicationAccessPolicy — 2 entries (BFF app-reg + UAMI) | `{PASS/FAIL}` | `{Get-ApplicationAccessPolicy output / log path}` |
| T5 | Slot-parity KV RBAC (or structurally-impossible via UAMI) | `{PASS/FAIL/N-A-structural}` | `{az role assignment list output / log path}` |
| T6 | SPE container creation via confidential-client (no delegated 403) | `{PASS/FAIL}` | `{az rest containerType GET output / log path}` |

**Traps summary**: `{N}/6 cleared`. <!-- Should be 6/6 for a clean acceptance -->

---

## Per-Invariant Verified (I1–I5)

| Invariant | Description | Verdict | Evidence |
|---|---|---|---|
| I1 | No hardcoded default tenant in provisioning scripts | `{PASS/FAIL}` | `{grep output + ArchTest result}` |
| I2 | AI Search queries include unconditional `tenantId` filter | `{PASS/FAIL}` | `{App Insights trace + ArchTest result}` |
| I3 | Cosmos reads/writes include partition-key predicate | `{PASS/FAIL}` | `{az cosmosdb sql query RU-charge + ArchTest result}` |
| I4 | SPE container ID always tenant-scoped-derived | `{PASS/FAIL}` | `{KV secret + Dataverse env-var match + ArchTest result}` |
| I5 | Graph token acquisition per-tenant scoped | `{PASS/FAIL}` | `{ArchTest result + token tid decode}` |

**Invariants summary**: `{N}/5 sample-verified`. <!-- Should be 5/5 -->

---

## Naming-Conformance Verdict

```
{paste the full pwsh -File scripts/naming-conformance-check.ps1 -Scope r1-owned output here}
```

| Field | Value |
|---|---|
| Exit code | `{0 / non-zero}` |
| Verdict | `{PASS/FAIL}` |
| Non-conforming items (if any) | `{list, or "none"}` |

---

## Cost Snapshot

| Field | Value |
|---|---|
| H0 preflight estimated cost | `${amount}/mo` |
| Actual cost (24-48h extrapolated via Cost Management) | `${amount}/mo` <!-- fill in 24-48h after run; may require a follow-up edit to this report --> |
| Target (Model 2 primary path) | `≤$400/mo` |
| Deviation | `{N% over/under target}` |
| Verdict | `{PASS / DRIFT-FLAGGED (>20% over) / FAIL}` |
| Cost breakdown by SKU (if drift flagged) | `{table or list of top-cost resources}` |

---

## Manual Gates Encountered

<!-- List every WaitingOnGate the run hit, how it was resolved, and how long the wait was. -->

| Gate | Handler | Wait duration | Resolution |
|---|---|---|---|
| `{e.g. Model 2 admin consent}` | `{H0.5}` | `{duration}` | `{customer admin clicked URL at {timestamp}; HMAC callback auto-detected}` |
| `{e.g. Azure quota bump}` | `{H1 or other}` | `{duration}` | `{if encountered — else omit row}` |
| `{e.g. SPE 24h replication}` | `{H8}` | `{duration}` | `{if encountered — note whether acceptance was completed before or after the 24h wait, or whether H8.a auto-resumed}` |

---

## Registry State (`sprk_dataverseenvironment` post-provision)

<!-- Query the record directly and paste the relevant fields -->

| Field | Value |
|---|---|
| `sprk_dataverseenvironmentid` | `{GUID}` |
| `sprk_provisionedon` | `{timestamp}` |
| `sprk_currentrunid` | `{should be null/cleared post-completion}` |
| `sprk_bffversion` | `{version}` |
| `sprk_solutionversion` | `{version}` |
| `sprk_tenantid` | `{GUID — must equal the customer tenantId supplied at intake, per I1}` |
| `sprk_setupstatus` | `{200000004 / Ready}` |

---

## Deviations / Lessons Learned

<!-- Any manual gate that took longer than expected, any handler that needed a resume, any drift
     discovered mid-run, any decision that deviated from the runbook, and why. Use the CLAUDE.md §6.5
     format if an ADR conflict surfaced. -->

`{free-text — list each deviation with a short rationale}`

---

## Model 1 Discretionary Run (if performed)

<!-- Per the Path A exception, Model 1 is now discretionary. If performed, summarize briefly here
     (full detail can point to a second report file if a full Model 1 run was also done). If not
     performed, state the skip rationale explicitly. -->

`{Either: "Not performed. Skip rationale: {reason}." OR a summary of the Model 1 dry-run/full-run
result + confirmation that §4.1a differences (H0/H2a/H2b/H4/H7/H10/H12c/H13 behavior deltas) held.}`

---

## Teardown Checklist (for after acceptance is verified)

<!-- Per plan.md Phase F Deliverables, teardown is discretionary — the trial stamp may be left for
     reference. If the owner chooses to tear down, use this checklist. -->

- [ ] Confirm this report is complete and committed before tearing down (evidence trail must survive teardown)
- [ ] Run `scripts/Decommission-Customer.ps1` (or the current decommission entry point) against `{customerId}` — NOTE: decommission is out of scope for r1 (D17); this is a manual/future-project action
- [ ] Verify `sprk_currentrunid` is cleared before decommission (avoid orphaned concurrency lock)
- [ ] Confirm KV secrets for `{customerId}` are soft-deleted (recoverable) not hard-deleted, per standard KV retention
- [ ] Update `sprk_dataverseenvironment.sprk_setupstatus` to reflect decommissioned state if the schema supports it
- [ ] Note final actual cost incurred for this acceptance run (for portfolio cost tracking)
- [ ] If left standing for reference (not torn down): note the retention decision + expected teardown date here: `{date or "indefinite — reference stamp"}`
