# Handler Log — {customerId}-{runId}

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> Analog of coding `current-task.md`. **Appended in real time** by `/provision-environment` skill after each handler completes.
> Do NOT rewrite prior entries — append only. Corrections go in [`manual-gates.md`](manual-gates.md) with rationale.

## Run summary

- runId: `{runId}`
- customerId: `{customerId}`
- Started: `{startTime}`
- Ended: `{endTime}` (if complete)
- Status: `{InProgress | Ready | Failed | Rolled-Back}`
- Handlers executed: `{N}` of `{expectedN}`

---

## Handler H0 (Preflight)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed | WaitingOnGate}`
- Cosmos-run-id: `{cosmos-doc-id}`
- Notes: {any operator-visible notes; link into Cosmos state for full detail}

## Handler H0.5 (Admin-consent gate)

- Started: `{ts}`
- Gate opened: `{ts}` — operator asked to grant `/adminconsent`
- Gate closed: `{ts}` — operator granted / declined / abandoned
- Status: `{Success | Cancelled}`
- Notes: {operator UPN, admin-consent URL, redirect landing}

## Handler H1 (Quota check)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed | WaitingOnGate}`
- Regions checked: {list}
- Quota headroom: {snapshot}
- Notes: if `WaitingOnGate` — operator opened Azure support ticket → append URL

## Handler H2a (Bicep infra deploy)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- Resources provisioned: `{N}`
- Bicep stack: `{stack-name}` (model1-shared / model1-customer / model2-full / customer)
- Duration: `{N} min`
- Notes: {any errors; deploymentId; RG name}

## Handler H2b (AI Search index deploy)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- Indexes deployed: `{N} of 7 canonical`
- Notes: {deployment script version; any index rebuild required}

## Handler H3 (Auth ceremony — BFF app-reg + client-cred)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- Notes: {app-reg objectId; client-cred rotation timestamp; per ADR-028 21 MUSTs applied}

## Handler H4 (KV secret seeding — per-tenant + shared)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed | Partial}`
- Secrets written: {N of expected}
- Drift detected: `{Y | N}` — if Y, cite which secret + old-vs-new age
- BINDING guard: `Dataverse-ClientSecret` + `BFF-API-ClientSecret` NOT touched ✓
- Notes: {source-service extractor results per per_env_settings manifest}

## Handler H4b (Bulk app-settings apply)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed | Partial}`
- Settings applied: {N} in ONE batch call (kills progressive-fail-fast chain)
- BFF /healthz: `{200 (green) | 503 | timeout}` after 8-min backoff
- Notes: {diff-first result — how many settings differed from live; container docker-log module cited if fail}

## Handler H5 (Dataverse env create)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- Dataverse env URL: `{url}`
- Notes: {`sprk_dataverseenvironment` placeholder promoted from Step 1 pre-POST to real state}

## Handler H6 (Solution import — dependency-ordered)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- Solutions imported: `{N} of 8` (per §11.1a dep-order)
- Notes: {any per-solution import warnings}

## Handler H7 (MI-Dataverse-App-User + scoped role)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- Notes: {App User created; `Spaarke Provisioning Registry` role assigned}

## Handler H8 (SPE container-type + published)

- Started: `{ts}`
- Gate opened: `{ts}` — 24h wait period (per PRQ documentation; in practice near-instantaneous per user memory)
- Gate closed: `{ts}`
- Status: `{Success | Failed | Cancelled}`
- Container-type ID: `{guid}`
- Notes: {publish confirmation via `Get-SPEContainerType`}

## Handler H9 (BFF zip deploy)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- Artifact size: `{MB}` (must be ≤60 MB per CLAUDE.md §10 NFR-01; report absolute + delta vs prior baseline)
- Slot: `{staging | production}`
- Notes: {version + git SHA + `pipeline-run` link}

## Handler H10 (Dataverse setup registry update)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- `sprk_dataverseenvironment.sprk_setupstatus`: `Ready`
- Notes: {timestamp of Ready state per FR-18}

## Handler H11 (Optional user provisioning)

- Started: `{ts}`
- Gate opened: `{ts}` — operator asked whether to bootstrap first admin user
- Gate closed: `{ts}` — operator provided UPN / license / skipped
- Status: `{Success | Failed | Skipped}`
- Users created: `{N}`
- Notes: {UPN(s), license SKU(s)}

## Handler H12a/b/c (AI seed chain)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- H12a (playbook consumers seed): `{count}`
- H12b (config env-vars seed): `{count}`
- H12c (post-config verify): `{Pass | Fail}`
- Notes: {any embedding-index seeding; per ADR-039 `spaarke-playbook-embeddings` retired — must NOT seed}

## Handler H13 (E2E acceptance probes)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed}`
- Probes run: T1-T6 (silent-fail traps) + I2-I5 (tenant-isolation invariants) — see task 204c
- Notes: {any probe that flipped RED; per punch list HARD BLOCKER for FR-18 acceptance}

## Handler H14 (Email-processing webhook wire-up)

- Started: `{ts}`
- Completed: `{ts}`
- Status: `{Success | Failed | Skipped}`
- Notes: {webhook signing key + client state; sidecar reachability}

---

## Handoff

When status = Ready → skill Step 6 writes [`handoff-report.md`](handoff-report.md).
When status = Ready OR Failed → skill Step 7 writes [`lessons-learned.md`](lessons-learned.md).
Both are mandatory before folder is committed + INDEX.md is updated.
