---
name: provision-environment
description: Provision a new Spaarke customer environment end-to-end via L2 control-plane REST API — prereqs, interactive intake, preflight, confirmation gate, execute loop with poll + manual-gate handling, and handoff report
tags: [provisioning, l3-skill, l2-controlplane, deploy, operations, customer-onboarding]
techStack: [powershell, azure-cli, pac-cli, dataverse-mcp, curl, azure]
appliesTo: ["provision-environment", "provision customer", "new customer environment", "customer stamp", "provision {customerId}", "customer-provisioning"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-11-03
---

# Provision Environment

> **Category**: Provisioning
> **Last Reviewed**: 2026-11-03
> **Procedure**: [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) (operator runbook)

Interactive Claude Code skill for provisioning a **new Spaarke customer environment** — a dedicated stamp in either
model since D-12 (Model 1 in Spaarke's tenant, Model 2 in the customer's) — end-to-end via the **L2 control-plane REST
API**. The skill is the **operator UX layer**: prerequisite checks, intake, preflight, confirmation gate, execute loop
with poll + manual-gate handling, handoff report and postmortem. The provisioning itself is done by L2
(`Sprk.Provisioning.ControlPlane` — Service Bus enqueue, Cosmos state, `IJobHandler` handlers H0–H14). The skill is
thin: it drives the operator experience, never the provisioning logic.

**Trigger phrases**: `/provision-environment {customerId}`, `/provision-environment` (prompts for customerId),
"provision new customer", "provision customer {name}", "new customer environment", "new customer stamp".
Flags: `--batch <intake.json>` (unattended, Step 1.0), `-SkipStep0_5` / intake `skipExternalPrereqs` (Step 0.5),
`--resume <runId>`.

## Quick Reference

| Item | Value |
|------|-------|
| L2 API base | `https://spaarke-provisioning-controlplane-{dev,demo,prod}.azurewebsites.net` |
| L2 REST surface | `POST /api/runs`, `GET /api/runs/{id}`, `POST /api/runs/{id}/resume`, `POST /api/runs/{id}/gates/{gateId}/advance`, `POST /api/runs/{id}/preflight`, `POST /api/runs/{id}/cancel`, `POST /api/runs/{id}/clear-quarantine` — every call carries `?customerId=` |
| L2 audience (token) | `api://spaarke.com/provisioning-controlplane-{env}` |
| Operator role required | `Operator` app-role (mutating) OR `Reader` (poll-only) |
| Handler catalog | 20 dispatchable ids (`HandlerIds.Dispatchable`): H0 / H0.5 / H1 / H2a / H2b / H3 / H4 / H4b / H5 / H6 / H7 / H8 / H9 / H10 / H11 / H12a / H12b / H12c / H13 / H14. Model 1 runs 19 (skips H0.5); Model 2 runs 20. H11 runs on EVERY run, both models (`DagAdvancer` has no skip; H12a/H12b depend on it). H4-shared retired (T226). Guide §H0–H14 |
| Trap catalog | T1–T7 (design §4B) — each handler asserts its trap clear before reporting success |
| Tenant-isolation invariants | I1–I5 (design §4D) — ArchTests; I2–I5 sample-verified at H13 (I1 is a build gate — T230a) |
| Wall-clock (fresh stamp) | ≤ 1 h (NFR-03) without lead-time gates (Azure quota / SPE / customer admin consent) |
| Cost envelope | ≤ $400/mo empty (NFR-04; ≈ $337 fixed at 2026-10-06 prices — T229). H0 refuses an estimate above the tier ceiling (smb $700 / enterprise $2,500 / dedicated $5,000); H13 flags > 20% drift |
| Handoff report | `runs/{runId}.md` in the operator's cwd (NOT under `.claude/`) |
| Scripts | `scripts/provision-environment/*.ps1` (run driver) + `scripts/provisioning-prereqs/Invoke-PrereqPass.ps1`, `Test-SpeTopology.ps1` |

## Critical Rules

### MUST:
- **MUST** run all Step 0 prerequisite checks BEFORE any intake or preflight; any failure is a hard stop
- **MUST** authenticate as the **operator's own AAD identity** (`az login`) — NEVER a service principal (NFR-11 auditable operator action)
- **MUST** call L2 with `Authorization: Bearer <token>` from `az account get-access-token --resource api://spaarke.com/provisioning-controlplane-{env}` (the scripts do this)
- **MUST** enqueue via L2 (`POST /api/runs`) and poll (`GET /api/runs/{id}`) — NEVER invoke handlers directly or reach into BFF Service Bus
- **MUST** require the **explicit phrase** `proceed with provisioning` at the Step 3 gate — a bare "y" or "yes" is INSUFFICIENT
- **MUST** surface manual gates with actionable instructions (URL to click, `az` command to run) — never fake progress past a gate
- **MUST** produce the handoff report `runs/{runId}.md` in the operator's working directory on every terminal outcome (success OR failure)
- **MUST** update the `sprk_dataverseenvironment` registry on run completion via the Dataverse MCP — Fallback F1 if it is disconnected
- **MUST** apply canonical KV secret naming per the FR-35 pre-check protocol — check LIVE App Service + KV + Dataverse before removing any alias
- **MUST** follow the KV credential-lifecycle rule: **`BFF-API-ClientSecret` is GONE** (auth-v4 task 033 deleted both KV copies `BFF-API-ClientSecret` + `bff-api-client-secret` and all 4 App Service settings; `Graph:Credentials:Order = [ManagedIdentityFederated]`, `RequireSecretFreeIdentity=true`). Do NOT re-introduce it under any name — `CredentialGuardTests` fails the build on any new `.WithClientSecret(...)`. H4 **omits** it unconditionally. `Dataverse-ClientSecret` never-delete rule STILL in force until 2026-11-23 (auth-v4 owns its retirement). Apply the current text of [`.claude/constraints/provisioning.md`](../../constraints/provisioning.md) §KV credential lifecycle, not this summary
- **MUST** run each step's script with `pwsh -NoProfile -File <script> -SessionFile <session>` and branch on its exit code (below) — never retype the logic inline

### NEVER:
- **NEVER** skip Step 0 prereqs — operator machines drift and silent tool-version mismatches cause silent-fail traps
- **NEVER** authenticate as a service principal — the L2 audit trail requires operator identity per NFR-11
- **NEVER** invoke handlers directly (bypassing L2 orchestration) — it breaks the state machine + idempotency + rollback taxonomy
- **NEVER** advance past a `WaitingOnGate` without the gate actually clearing — `POST .../gates/{gateId}/advance` makes L2 re-verify; the operator's "I did it" is not evidence
- **NEVER** hardcode or default a tenant ID — every intake carries an explicit `tenantId` (I1; `Register-EntraAppRegistrations.ps1:63` fix `1834b77bc`)
- **NEVER** proceed with a `Quarantined` run, and never call `clear-quarantine` on the operator's behalf — it needs a human-accountable reason
- **NEVER** run two concurrent runs for one `customerId` — L2 returns 409 (optimistic concurrency on `sprk_currentrunid`); respect it
- **NEVER** invent a customerId — the operator supplies it and Step 1a validates it
- **NEVER** auto-retry a `Failed` run (batch `onFailedPolicy=autoResumeOnce` is the one sanctioned exception) — auto-retry hides operator-actionable diagnostics
- **NEVER** use `Read-Host` or any console prompt — the PowerShell tool has no stdin. Ask the operator in chat, then pass the answer to the script

## How the scripts keep state

No PowerShell variable survives between tool calls, so state lives in two files (both ignored by git):

- **Session file** `runs/provision-session-{timestamp}.json` — created by `New-ProvisionSession.ps1`, which prints
  `SESSION=<path>`. Pass that path as `-SessionFile` to every later script. Holds mode, control-plane env,
  environmentId, runId, the confirmation phrase, MCP-fallback and registry-stale flags, prerequisite results. No
  personal data. Keep the path in your notes — after a compaction, find the newest `runs/provision-session-*.json`.
- **Intake file** — batch: the operator's file (never edited; its SHA-256 is the audit record). Interactive: write the
  operator's answers to `runs/{customerId}-intake.json` in the [`intake.schema.json`](../../../scripts/provisioning-prereqs/intake.schema.json)
  shape (no `confirmationAcknowledgment` — Step 3 records that) and register it with `Set-ProvisionSession.ps1 -IntakeFile`.

| Exit | Meaning | Do |
|---|---|---|
| 0 | passed / done | next step |
| 1 | HARD STOP | report the message + resolution; stop (then Step 7 if a run exists) |
| 7 | interactive: value needed (`FIELD=<name>` printed) | ask the operator for that value, update the intake file, rerun the same script |
| 2 / 3 / 4 / 5 | run Failed / Quarantined / WaitingOnGate / Cancelled | Step 4b / Step 5 |
| 5 (from `Write-HandoffReport -Action Check`) | registry stale — both artifacts written | Step 6d warning variant |
| 6 | batch postmortem file invalid | Step 7b |
| 9 | L2 unreachable after one retry | Fallback F3 |
| 10 | poll window elapsed, run still in progress | call `Watch-ProvisioningRun.ps1` again |

All paths below are relative to the repo root (the operator's cwd); `$S` is the session path.

## Procedure

- [ ] Step 0 prerequisites (+ 0d MCP) → [ ] 0.5 external prerequisites + 0.5c SPE topology → [ ] 1 intake (1a–1g)
- [ ] 2 client-side preflight (2.5 when fresh subscription) → [ ] 3 confirmation gate → [ ] 4 POST + poll
- [ ] 5 manual gates (as they arise) → [ ] 6 registry + handoff (+ 6e, 6f) → [ ] 7 postmortem (ALWAYS)

### Step 0: Prerequisites Check (Automated) — HARD STOP on any failure

**Batch mode: run Step 1.0 first** — it binds the env and the batch policies that Steps 0d and 0.5a read.
Interactive: the checks need the control-plane env; use the operator's argument, else ask now (`dev` | `demo` |
`prod` — the same answer as Step 1d; `dev` was the original placeholder). Create the session:
`pwsh -NoProfile -File scripts/provision-environment/New-ProvisionSession.ps1 -Mode Interactive -Env <env>`.

#### 0a. Tool version checks
#### 0b. Operator AAD identity
#### 0c. L2 API reachability + Operator role
#### 0e. Working directory + git state
#### 0f. L2 deployment probe
#### 0g. Report + gate

All five run in one script and print the 0g checklist:
`pwsh -NoProfile -File scripts/provision-environment/Test-OperatorPrereqs.ps1 -Env <env> [-Batch]`.
0a: pwsh ≥ 7.4, az ≥ 2.60, pac ≥ 1.35, git ≥ 2.40 (batch adds COMP-15: `az account show` < 5 s). 0b: Spaarke tenant,
a user — never a service principal. 0c: `/healthz` + a READ-ONLY role probe (`GET /api/runs/{random}?customerId=__role-probe__`
— 404 proves Reader/Operator, 401 → `az login`, 403 → no role: grant at least Reader; never probe with a POST, ISH-10).
Operator role is proven only by the Step 4 POST (a 403 there IS the signal). 0e: a git repo (warning only). 0f: L2 App
Service `Running` (else HARD STOP); build-tag mismatch is a warning until the deploy pipeline stamps one. Exit 1 → stop.

#### 0d. Dataverse MCP status (optional but strongly recommended)

Ping with `mcp__dataverse__describe` on `sprk_dataverseenvironment`. Record the result:
`Set-McpStatus.ps1 -SessionFile $S -Alive` when it answered. When it did not — interactive: show
"⚠ Dataverse MCP is not connected. Impact: registry updates on run completion will use the fallback matrix (Web API) —
slower but functional. Continue anyway? (yes/no)" and pass `-OperatorAnswer <answer>`; batch: run it without `-Alive`
— `mcpDisconnectPolicy` (default `failFast`, BAT-04) stops with `runs/pre-dispatch-mcp-disconnect.json` or proceeds on
the fallback. Not a hard stop in interactive mode; batch defaults to fail-fast because unattended runs need up-front reliability.

### Step 0.5: External Prerequisites Iteration (per `scripts/provisioning-prereqs/prereqs.yaml`) — HARD STOP on any failure

Iterates the manifest DYNAMICALLY — new prereqs are picked up without a SKILL.md edit. Skipped only with
`-SkipStep0_5` / `"skipExternalPrereqs": true` (interactive: `Set-ProvisionSession.ps1 -SkipExternalPrereqs`) when the
operator has checked every applicable prereq by hand per [`PROVISIONING-PREREQUISITES.md`](../../../docs/guides/PROVISIONING-PREREQUISITES.md);
recorded in Step 7. Silent skip is FORBIDDEN.

#### 0.5a. YAML parser + environment fail-fast
#### 0.5b. Iterate the manifest
#### 0.5d. Report + HARD STOP

`pwsh -NoProfile -File scripts/provisioning-prereqs/Invoke-PrereqPass.ps1 -SessionFile $S -Pass Env` — installs
`powershell-yaml` if needed (0.5a), checks the env (COMP-14: batch HARD STOPs on a null/invalid env), derives the token
map, runs `once_per_tenant` + `once_per_subscription` + `once_per_env` recipes under `bash -c` (0.5b) and prints the
0.5d checklist (id, output, consequence, remediation link `PROVISIONING-PREREQUISITES.md#<id>`). Exit 1 → HARD STOP.
**Recipe author contract** (BINDING for every `prereqs.yaml` entry): pass/fail is the recipe's EXIT CODE — it MUST
`exit 1` on every failure it detects (task 206; `validate.ps1` lints it); `check_recipe.expect` is human-readable only;
a new `{token}` REQUIRES extending the token map in `Invoke-PrereqPass.ps1` (and `spaarke-constants.yaml` /
`context-defaults.*.json`) — otherwise the pass fails with `[skill-config] unresolved placeholder` (PLX-14) before bash runs.

#### 0.5c. SPE topology verify — HARD STOP on missing owning-app / container-type

`pwsh -NoProfile -File scripts/provisioning-prereqs/Test-SpeTopology.ps1 -SessionFile $S` — the owning app-reg and the
container type must exist before any dispatch ([topology §3A](../../../docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md)).
A 403 on the container-type read is expected (CLI token not consented, T248) and skips the owning-app check — H0's
`SpeOwnerCredential` check proves it. There is no shared BFF app-reg to check (H3 creates one per customer — T227a).
Escalate: a 404 persisting > 30 min after creation; an owning app that no longer exists (the binding is immutable —
the container type is unusable; operator emergency).

### Step 1: Interactive Intake

**In interactive mode, read [`references/intake-fields.md`](references/intake-fields.md) before asking the operator
for intake values** — it holds each field's meaning, the operator-facing explanations and the full rules. Ask one
field (or group) at a time, write it into `runs/{customerId}-intake.json`, and validate with
`Test-ProvisionIntake.ps1 -SessionFile $S -Step <ids>` (exit 7 → ask again; exit 1 → HARD STOP). Batch mode runs the
same script with `-Step all` and every failure is a HARD STOP.

#### 1.0 Batch mode (`--batch <path.json>`)

`New-ProvisionSession.ps1 -Mode Batch -BatchIntakeFile <path> -Env <controlPlaneEnv>` validates the intake against
`intake.schema.json` (ajv + ajv-formats via npx), refuses a file git would track (personal data — D15), requires
`confirmationAcknowledgment` = `proceed with provisioning` (BAT-03), and binds the env, environmentId, skip flag and the
BAT-04..09 policies. Every 1a–1e-bis prompt is then non-interactive; 1f still runs when `environmentId` is absent.

#### 1a. `customerId` (required)
The customerId standard `^[a-z][a-z0-9]{2,7}$` — no hyphens, not `platform`/`shared`/`byok`; abbreviate once, here.
Validate (`-Step 1a`), then the **uniqueness / upgrade probe**: `mcp__dataverse__read_query` on
`sprk_dataverseenvironment` filtered by `sprk_customerid` (attributes `sprk_dataverseenvironmentid`,
`sprk_provisionedon`, `sprk_setupstatus`), then `Invoke-RegistryStep.ps1 -SessionFile $S -Action Probe -ProbeJson '<rows>'`
(MCP down → `-UseWebApi`). `REGISTRY=new` → fresh run. A row with `sprk_provisionedon` → UPGRADE: interactive asks
"Continue as UPGRADE run? (yes/no)" (exit 7 → ask, rerun with `-OperatorAnswer`); batch needs
`acknowledgeUpgradeMode` (BAT-05). `REGISTRY=in-progress` → Fallback F1 recovery path.

#### 1a-bis. `displayName` · 1b. `tenantId` · 1b-bis. `subscriptionId` and `dataverseEnvUrl` · 1b-ter. `tier` and `estimatedMonthlyUsd` · 1b-quater. `openAiMonthlyLimitUsd` · 1b-quinquies. `solutionPackageType` · 1b-sexies. `secureRecordSetupDryRun` · 1c. `tenancyModel` · 1d. `environment` · 1e. `profile`

Validated by `-Step 1a-bis,1b,1b-bis,1b-ter,1b-quater,1b-quinquies,1b-sexies,1e`. Hard rules: `tenantId` is NEVER
defaulted (I1). `subscriptionId` is the customer's OWN subscription and `dataverseEnvUrl` the environment the operator
created — never `az account show` (T228). Do not ask about `solutionPackageType` (managed unless the owner says
unmanaged) or `secureRecordSetupDryRun` (only on request). `tenancyModel` ↔ `profile` are 1:1: `Model1` ↔
`spaarke-hosted-model2`, `Model2` ↔ `customer-owned-model2` (Model 2 is out of scope now); `spaarke-hosted-model1-trial`
is RETIRED — never "fix" the profile literals. 1d (`dev`/`demo`/`prod`) is the session env: confirm it matches Step 0
(`Set-ProvisionSession.ps1 -Env`).

#### 1e-bis. Users, Exchange scope group, Graph resources, default mailbox (required — T245c)

`-Step 1e-bis` — runs BEFORE 1f so a bad value stops the skill before it writes the registry. Model 1 takes only
`B2BGuest`; B2BGuest also checks, live and read-only as the operator, the PRQ-C-14 attestation, the
`sprk-{customerId}-users` group (PRQ-C-10, including that it is the group SET ON the environment), guest access
(PRQ-C-12) and PAYG billing on the stamp subscription (PRQ-C-11). **Personal data**: never print or commit the user
list; `provisioning-runs/{run}/intake.md` records the user COUNT only.

#### 1e-ter. Customer prerequisites — the `once_per_customer` pass

`Invoke-PrereqPass.ps1 -SessionFile $S -Pass Customer` — same rules and HARD STOP as Step 0.5, BEFORE 1f writes
anything (skipped with `-SkipStep0_5`). Recipes needing the customer's subscription run with the operator's az sign-in,
which must reach it; PRQ-S-06 needs Reader on the `spaarke-customers` management group.

#### 1f. `environmentId` — create placeholder `sprk_dataverseenvironment` record

`POST /api/runs` REQUIRES it. Skip when the probe found the row (upgrade) or the intake carried it. Registry env:
`spaarkedev1` for `dev` (and for the demo, which is the ACME stamp on the dev control plane); prod registry not yet
provisioned. `Invoke-RegistryStep.ps1 -Action Plan` prints the payload → `mcp__dataverse__create_record` (MCP down:
`-Action CreateWebApi`). Then read the row back by `sprk_customerid` and run `-Action Confirm -EnvironmentId <id>
-ProbeJson '<rows>'` (or `-UseWebApi`) — PRQ-05: the alt-key must return the same GUID, or CustomerRunGuard 409-loops.
Never add `sprk_profile` or `sprk_upgrademode` (deprecated). Field details: `references/intake-fields.md` §1f.

#### 1g. Show intake summary

Show customerId, displayName, tenantId, tenancyModel, controlPlaneEnv, profile, subscriptionId, dataverseEnvUrl,
environmentId (placeholder, setupstatus 1 InProgress), identityPreset, user COUNT (never names/emails), env group
(PRQ-C-10/12/11 results), Exchange group, default mailbox, L2 base. Interactive: ask "Proceed to preflight (H0)?
(yes/no)" and wait for "yes" (bare "y" is insufficient at every gate — spec §4.3a.4). Batch: print it, no prompt
(BAT-02 — the intent is attested by `confirmationAcknowledgment`).

### Step 2: Client-side Dry-Run + Preflight Planning (no server mutation)

Step 2 is CLIENT-SIDE ONLY. There is no preflight-only run mode: `CreateRunRequest` has no `mode` field, and H0 runs
as the first handler of the single Step 4 POST (EXEC-02 / ISH-03). H0-only re-verification on an EXISTING run:
`POST /api/runs/{runId}/preflight?customerId=` (upgrade use case). **If the target subscription is < 90 days old, or
this is the first Bicep deploy into it in this region, read [`references/fresh-sub-feasibility.md`](references/fresh-sub-feasibility.md)
(Step 2.5) before continuing.** Then, with nothing mutating on L2 or in Azure:
- re-run the customer-run history probe (Step 1a semantics — `Invoke-RegistryStep.ps1 -Action Probe`) to catch an
  upgrade or a run started since Step 1a;
- prereqs scoped `once_per_customer_pre_intake` run again here (none in the current manifest — reserved, EXEC-10);
- `Test-CostEnvelope.ps1 -SessionFile $S` (BAT-10/T229 — estimate vs tier ceiling; no override; diagnostic
  `runs/pre-dispatch-cost-overrun.json`);
- build and show the plan for Step 3 (handler list per profile, cost, duration). A batch intake was schema-validated
  at Step 1.0 (an interactive intake cannot pass the schema before Step 3 — MOVE-MAP D14).

On any failure present it with escalation instructions and do NOT proceed to Step 3.

### Step 2.5: Fresh-Sub Deployment Feasibility Check
Conditional — see Step 2. Checks run against the customer's subscription, never the operator's current one (T228).

### Step 3: Confirmation Gate — explicit "proceed" phrase required

**Nothing mutates the target env until the operator types the exact phrase.** Present the run plan:

```
RUN PLAN
  customerId / tenantId / tenancyModel / profile / subscriptionId / dataverseEnvUrl / environmentId
  Handlers (one set for both models; D-12): H0.5 consent (Model 2 only) · H1 resource group ·
    H2a Bicep apply (30-min timeout) · H2b AI Search indexes (7) · H3 per-customer BFF app registration (FIC, T240a) ·
    H4 per-tenant KV secrets · H4b bulk app settings (one batch, one restart; F20) · H5 adopt the operator's Dataverse
    environment (never creates — T228) · H10 application users + Graph parity (before H6) · H6 SpaarkeMaster import
    (managed by default — T218b) · H7 env vars · H8 SPE root container (T227e) ·
    H9 BFF deploy (staging slot, after H4 + H4b) · H11 users (identityPreset + users) · H12a AI seed · H12b playbook
    consumers · H12c agents · H13 acceptance (traps, invariants, keyless stamp, cost) · H14 Exchange mailbox roles (T4)
  Estimated wall-clock: ~42 min without lead-time gates   Estimated cost: ${estimate}/mo (tier '{tier}', ceiling ${cap}/mo)
  Manual gates you MAY meet: H0.5 Model 2 admin consent · H1 Azure quota bump (advance re-runs the verifier) ·
    H8 SPE replication (near-instant in practice; 25 h fallback ceiling)
  DESTRUCTIVE OPERATIONS: none in fresh-provisioning mode. Upgrade mode may overwrite Bicep-managed resources with
    drift; H2a's what-if diff is presented separately before applying.

To proceed with this run, type the exact phrase:
    proceed with provisioning
(a bare "y" or "yes" is NOT accepted at this gate — spec §4.3a.4)
```

Pass what the operator typed, verbatim: `Confirm-ProvisionGate.ps1 -SessionFile $S -Phrase '<text>'`. Exit 7 → anything
else ("y", "yes", "go", "ok") — re-ask the same gate. Batch (BAT-03): run it without `-Phrase`; it re-checks the
intake attestation and records the intake SHA-256. The phrase is captured in the run's audit trail (NFR-11).

### Step 4: Execute — issue THE single POST + poll → advance

#### 4.0. Enqueue
`Start-ProvisioningRun.ps1 -SessionFile $S` — refuses without the Step 3 phrase or the Step 1f environmentId; builds
`CreateRunRequest` (exactly `customerId, environmentId, tenancyModel, profile, nonSecretParameters`; the
`nonSecretParameters` keys are a CLOSED SET — `IntakeParameterCatalog`, else 400 `intake-unknown-key`) and POSTs once.
Prints `RUN_ID=`. 403 → the operator lacks the Operator role; 409 → a run already holds the customer — never POST a
second one. L2 then auto-advances the DAG; `/resume` is ONLY for a `Failed` run.

Re-attaching to a run that already exists (`--resume <runId>`, a batch `WAITING.md`, a Fallback F3 re-invocation, or
Step 1a `REGISTRY=in-progress` — take the run id from the earlier session file, `runs/{runId}*.md|json` or
`sprk_currentrunid`): `New-ProvisionSession.ps1 ... -ResumeRunId <runId>`, then go straight to Step 4a.

#### 4a. Poll loop
`Watch-ProvisioningRun.ps1 -SessionFile $S` polls every 10 s for 9 min (exit 10 → call again; or run it in the
background with `-MaxMinutes 60`). It refreshes the token on 401 (3 strikes → stop, F2), retries a 5xx once (then exit 9
→ F3) and checks `/healthz` when the run stalls outside H2a/H8/H12a (EXEC-05 — there is no nudge for a stuck `Running`
run; escalate as F3). Track each handler in TodoWrite and report progress every ~5 handlers or on any state change.

#### 4b. Handle each terminal state (RunStatus enum per `ProvisioningRun.cs`)

| Status | Meaning | Skill action |
|---|---|---|
| `NotStarted` / `Running` | queued / handlers executing | keep polling |
| `WaitingOnGate` (exit 4) | handler paused on an external condition | Step 5 |
| `Completed` (exit 0) | all handlers done + H13 acceptance passed | Step 6 |
| `Failed` (exit 2) | `Retryable*` / `Resumable` failure (§4C). `secure_setup.dry_run` = the expected end of an H7b dry run: show the plan from gate `h7b-secure-setup-plan`; never resume it | Interactive: show phase + error, ask "Resume this run? (yes/no)"; yes → `Invoke-RunAction.ps1 -Action Resume`, then poll; no → abandoned, Steps 6b + 7. Batch: the script applied `onFailedPolicy` |
| `Cancelled` (exit 5) | `POST .../cancel`; `sprk_currentrunid` released | report; no auto-restart |
| `Quarantined` (exit 3) | `QuarantineRequired` failure | HARD STOP; `runs/{runId}-quarantine.json`; operator must `clear-quarantine` with reason + audit trail |

There is NO `Drifted` state — drift surfaces as `Failed` with `upgrade-drift-detected`. Every terminal outcome still
goes through Step 6b (report) and Step 7 (postmortem).

### Step 5: Manual Gate Handling

Batch: `Watch-ProvisioningRun.ps1` already applied `onManualGatePolicy` (`waitAndExit` → `runs/{runId}-gate.json` +
`runs/{runId}-WAITING.md`, exit 4; `pollUntilTimeout` → 30 min, 25 h for H8 SPE; `failFast` → exit 4). **Interactive:
read [`references/manual-gates.md`](references/manual-gates.md) before continuing** — it has the operator messages for
each gate. Advance ONLY with `Invoke-RunAction.ps1 -Action Advance -GateId <id>` when the operator says the action is
done: L2 re-verifies, and the run stays `WaitingOnGate` if the condition is not met. NEVER use `/resume` for a gate.
While H3 waits on consent, the Dataverse branch (H6 → H7) waits with it; only H2b and H5 keep moving.

#### 5a. H0.5 Model 2 admin consent (Model 2 only)
#### 5b. H1 Azure quota bump (rare)
#### 5c. H8 SPE container-type replication
#### 5d. Generic pattern for any other `WaitingOnGate`

### Step 6: Completion Handoff

#### 6a. Update `sprk_dataverseenvironment` registry — read then update

H13 writes Cosmos first and the registry best-effort (MED#10), so the row may be stale in any of four ways; this step
covers all of them and NEVER stops before 6b (HIGH#10). `Sync-RegistryOnCompletion.ps1 -SessionFile $S -Action Plan
-ObservedSetupStatus <value>` — first read the row with `mcp__dataverse__read_query` (`sprk_setupstatus`,
`sprk_provisionedon` by `sprk_dataverseenvironmentid`) and pass its `sprk_setupstatus`; the script prints the promoted
columns (adding `sprk_setupstatus = 2` only when not already Ready). Write them with `mcp__dataverse__update_record`.
MCP fails → `-Action ApplyWebApi`; the environmentId lookup fails → `-Action MarkStale -Diagnostic '<error>'`. NEVER
write `sprk_tenantid` (I1) or `sprk_currentrunid` (released only by `ICustomerRunGuard`); drift in a set-once column →
HARD STOP + escalate, never overwrite.

#### 6b. Write handoff report
#### 6c. Registry-stale diagnostic

`Write-HandoffReport.ps1 -SessionFile $S -Action Write [-Status <s>]` writes `runs/{runId}.md` (every token
substituted — PLX-12) and, when 6a marked the registry stale, `runs/{runId}-registry-stale.md` with the recovery
recipe. Fill every `<!-- FILL -->` marker from the run record and this session (handler outcomes, traps T1–T7,
invariants I1–I5, 6e/6f results, deviations and fallbacks used), then `-Action Check` (exit 1 while anything is
unfilled; exit 5 when the registry is stale — after both files exist).

#### 6d. Final summary to operator

```
✅ PROVISIONING COMPLETE — Customer {customerId} · Run {runId} · Duration {duration} · Status Ready
  Handoff report: runs/{runId}.md   Registry updated: sprk_setupstatus = Ready
  Post-completion checklist:
    [ ] Send customer welcome email (template docs/deployment/customer-comms/U-CB-01)
    [ ] Verify first user can sign in and load workspace
    [ ] Confirm cost drift alerts configured in Azure
    [ ] Update project #2 (portfolio board) with the new customer entry
    [ ] Copilot agent gate (6f): auth config recorded, package rendered and sent to customer IT
```

Registry stale (exit 5): "⚠ PROVISIONING COMPLETE (REGISTRY STALE)" — Status "Ready (L2), Registry PATCH FAILED
(operator-side)", both file paths, "MANUAL RECOVERY REQUIRED — see runs/{runId}-registry-stale.md. Customer-facing
functionality is operational; only operator dashboards + downstream automation affected."

#### 6e. Secure-record environment verification (MANDATORY before the customer is told the environment is ready — H7b, T256)

H7b creates, on every run between H6 and H9: the `Secure Record` business unit (no users, **no container**), the
`Secure Record Owners` team, the `Secure Record Owner` role inside that unit, and the BFF-managed field-security and
identity-link memberships. Run the **§7 verification checklist** of
[`SECURE-PROJECT-ENVIRONMENT-SETUP.md`](../../../docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md) against THIS
environment (it is the one source — do not copy its steps here) and record each result in `runs/{runId}.md`. Any item
not passing → report the environment as NOT secure-record ready (the BFF fails closed — nothing leaks — but no record
can be made secure). Containers are not part of this step.

#### 6f. Per-customer Copilot agent (MANUAL GATE after Ready — T257)

**When the run is Completed, read [`references/copilot-agent-gate.md`](references/copilot-agent-gate.md) before
continuing.** Interactive only; batch writes `runs/{runId}-copilot-gate.md` and marks it PENDING in the handoff.

### Step 7: Postmortem — write `lessons-learned.md` (MANDATORY)

Runs UNCONDITIONALLY after Step 6 — `Completed`, `Failed`, `Cancelled`, `Quarantined` or a manual abort — and before
the run folder is committed. Skipping is FORBIDDEN even for successful runs; an operator who declines still gets a
3-line file saying so, committed.

#### 7a. Copy template + prefill run metadata
#### 7b. Interactive postmortem (or batch mode: `intake.postmortemFile`)
#### 7c. Update `provisioning-runs/INDEX.md`
#### 7d. Report + commit gate

`Write-Postmortem.ps1 -SessionFile $S -Action Prepare -Outcome <outcome>` — 7a, plus the batch 7b path (`postmortemFile`
validated, exit 6 if missing or lacking a required section; else the auto-minimum). Interactive 7b: walk the operator
through each section — What went right (3–5 bullets citing handler + timestamp) · What went wrong (`### Lesson L01…`:
Symptom / Root cause / Fix applied / Landing spot / Blocks future runs / Punch-list class) · New prereqs to codify ·
New patterns to add · Recommendations for next run · Cross-run pattern · Sign-off — and include the Step 0.5 / 1e-ter
results the script prints. Then `-Action Finalize -Outcome <outcome>` (7c index row + 7d summary) and tell the operator
to commit `provisioning-runs/{customerId}-{runId}/` with the handoff report.

## Fallbacks

**If the Dataverse MCP disconnects, an L2 call returns 401 after a refresh, or L2 returns 5xx / times out, read
[`references/fallback-matrix.md`](references/fallback-matrix.md) (F1 / F2 / F3) before continuing.** The scripts already
apply the automatic part (Web API registry path, token refresh, one retry); the reference holds the operator prompts
and escalation.

## Gotchas

- **Interactive `$env` timing**: Step 0c/0f and 0.5c need the control-plane env, so interactive mode asks for it at
  Step 0 (Step 1d confirms). Without it `once_per_env` prereqs would never run; the Step 1e-ter pass runs them when
  Step 0.5 could not.
- **`sprk_setupstatus` is an option set** — write `2` (Ready) / `1` (InProgress), never the label; the Web API refuses
  `"Ready"`. Server code writes `2` (`DataverseEnvironmentRegistryClient`).
- **`pac data` has no query/create/update** (pac 1.46: export/import only) — the F1 fallback is the Web API.
- **`az.cmd` breaks on `)`** in arguments on Windows (`RetrieveCurrentOrganization(...)`) — get the token from az and
  call with `Invoke-RestMethod`. Prereq recipes must not put parentheses in az arguments (`validate.ps1` lints it).
- **`bash` on PATH can be the WSL stub** (System32 or WindowsApps) — `Invoke-PrereqPass.ps1` prefers Git Bash.
- **H14 `h14m-mailbox-ensure-failed` naming PRQ-E-16** ("A server side error has occurred … try again") is a 403
  from a role entry narrowed without the PowerShell common parameters (T186 L62) — do not wait it out; see
  `references/troubleshooting.md`.
- **MCP disconnect is common** (2026-08-14, 2026-08-15) — expected, not an error; F1 handles it.
- **The run state lives in Cosmos, not the session** — re-invoking for the same customerId detects the run; a lost
  session does not lose the run.

## References

- **Detailed references** (this skill): [`references/intake-fields.md`](references/intake-fields.md),
  [`references/fresh-sub-feasibility.md`](references/fresh-sub-feasibility.md),
  [`references/manual-gates.md`](references/manual-gates.md), [`references/copilot-agent-gate.md`](references/copilot-agent-gate.md),
  [`references/fallback-matrix.md`](references/fallback-matrix.md), [`references/troubleshooting.md`](references/troubleshooting.md)
  (also: tool matrix, auth flow, failure modes, related skills and docs)
- **Code**: `src/server/services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs` (routes),
  `…ControlPlane.Core/Models/ProvisioningRun.cs` (run record), `…/Models/IntakeParameterCatalog.cs` (accepted keys)
- **Spec / design**: `projects/customer-provisioning-orchestration-r1/spec.md` (FR-25, §4.3a, NFR-11), `design.md`
  (§4B traps, §4C rollback taxonomy, §4D invariants, §14A upgrade model)
- **Constraints**: [`.claude/constraints/provisioning.md`](../../constraints/provisioning.md)

*This skill is the operator's single entry point to customer provisioning. It wraps the L2 REST API — it does not
reimplement provisioning logic.*
