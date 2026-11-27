# Phase 0 spike report: engine hosting on dev (task 008)

Date: 2026-11-27. Owner OK: 2026-11-27, in chat ("Yes, deploy to dev"), covering the engine infrastructure, the ACR image push, the AcrPull grant and the agreement-edit-plan Action and Binding rows on dev Dataverse. Dev only.

This report holds ids, timings, hashes and outcome codes only. No clause or document text appears in it (ADR-015). Inputs were the synthetic corpus only (`src/accelerators/practices/nda/evals/corpus/nda-01/02/03-*.docx`); nothing under `answer-key/` was read.

## 1. How dev is deployed, and what was deployed

Dev is **not** a `customer.bicep` stamp. It is a hand-built set of resource groups:

| Thing | Dev resource | Resource group |
|---|---|---|
| BFF | `spaarke-bff-dev` | rg-spaarke-dev |
| BFF identity | `mi-bff-api-dev` (principal `9fd47efb-7962-492b-ac44-e5ccd0268ebb`) | spe-infrastructure-westus2 |
| Service Bus | `spaarke-servicebus-dev` (eastus, Standard) | SharePointEmbedded |
| Log Analytics / App Insights | `spe-logs-dev-67e2xz` / `spe-insights-dev-67e2xz` | spe-infrastructure-westus2 |
| Platform ACR | `sprkcontrolplanedevacr` | rg-spaarke-platform-dev |

`customer.bicep` at subscription scope would create a whole stamp, so it was not used. Instead `notes/spikes/dev-intake-engine.bicep` (dev-only wrapper, committed with this report) calls only the three task 002 modules (`uami.bicep`, `intake-staging-storage.bicep`, `intake-engine-job.bicep`) against the existing dev resources.

Placement choices on dev:
- Staging container in `sprkspaarkedevebillsa`: the BFF's only keyless storage account on dev (shared-key access off). It had **no** lifecycle management policy before, so the singleton `managementPolicies/default` now holds only the intake rule. Shared with eBilling's containers (`invoice-files`, `procedures`, `billing-exports`); a later eBilling lifecycle rule must be merged into this policy or it replaces the 7-day rule (task 002 note, deviation 5).
- Queues, environment and Job in `SharePointEmbedded`: the job module creates the queues on an `existing` namespace in its own resource group. Location westus2.

### What-if (hard rail: no non-intake create, modify or delete)

| Deployment | Result |
|---|---|
| Step 1, subscription what-if | Create: `mi-intake-engine-dev`, `spaarke-intake-cae-dev` + its diagnostic setting, both queues, BFF role assignments on both queues; 3 Unsupported = the engine identity's queue role assignments (principal known only at deploy time); 24 Ignore. The staging nested deployment was short-circuited (copy count depends on a runtime principal id), so it was what-if'd alone at resource-group scope with a placeholder principal: Create `intake-staging` container, 2 container-scope role assignments, `managementPolicies/default` (Create, none existed). No Modify, no Delete. |
| Step 3, subscription what-if | Create: `spaarke-intake-job-dev`. Modify: the environment, its diagnostic setting and the two queues, all intake resources, deltas only server-default properties (`peerAuthentication`, `metrics`, `autoDeleteOnIdle` and similar). No non-intake resource changed. |

### Resources created (ids)

| Resource | Id / value |
|---|---|
| Engine UAMI | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-dev/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-intake-engine-dev`; principal `7799afd6-3f45-4714-93f4-fe7266841339`; client `d2a7598c-f0a5-46f9-b961-31832275520e` |
| Container Apps environment | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.App/managedEnvironments/spaarke-intake-cae-dev` |
| Container Apps Job | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.App/jobs/spaarke-intake-job-dev` (event trigger, replicaTimeout 600 s, replicaRetryLimit 0, no ingress) |
| Request queue | `spaarke-servicebus-dev/queues/intake-engine-requests` (maxDeliveryCount 5, lock PT5M) |
| Completion queue | `spaarke-servicebus-dev/queues/intake-engine-completions` (maxDeliveryCount 5) |
| Staging container | `sprkspaarkedevebillsa/blobServices/default/containers/intake-staging` (private) |
| Lifecycle rule | `sprkspaarkedevebillsa/managementPolicies/default`, rule `delete-intake-staging`, 7 days after creation |
| AcrPull (step 2) | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-platform-dev/providers/Microsoft.ContainerRegistry/registries/sprkcontrolplanedevacr/providers/Microsoft.Authorization/roleAssignments/ef51b19a-0b5c-4f4e-be00-fb28d3b1850c` |
| ARM deployments | `intake-dev-step1` (16:24–16:29 UTC), `intake-dev-step3` (subscription scope, westus2) |

The engine identity holds exactly five grants (read back with `az role assignment list --all`): Blob Data Contributor on `intake-staging`; Service Bus Data Receiver + Sender on `intake-engine-requests`; Sender on `intake-engine-completions`; AcrPull on `sprkcontrolplanedevacr`. Nothing namespace-, account- or group-wide.

Spike-only operator grants (my user, for uploading inputs and reading completions): Blob Data Contributor on `intake-staging`, Service Bus Data Receiver on each intake queue. Removed at the end of the spike (section 9).

### Differences from the POML's three-step flow (for #3210)

1. Template: `dev-intake-engine.bicep` wrapping the three modules, not `customer.bicep` (dev is not a stamp).
2. Step 2 was done by this session under the owner's OK (the operator is Owner on the subscription), not by L2.
3. Step 3 image: there is no `provisioning-artifacts/intake-engine/latest.json` yet. The CI push leg cannot run from this branch: the workflow_dispatch run 46032612671 failed at "Azure login (OIDC)" (AADSTS700213; the federated credential trusts only `refs/heads/master` and the `dev`/`staging`/`production` environments). By design; no credential was added. The image used is a **spike image**, built and scanned in ACR Tasks (section 2). The CI-published image and `latest.json` arrive when the branch merges to master.

## 2. Image and Trivy

| Item | Value |
|---|---|
| Image | `sprkcontrolplanedevacr.azurecr.io/sprk-intake-engine@sha256:da01f1cf94c33e7ba5e296ba79b41bc2a6cf6a4d99dbff810e46cd11dd02fe1c` |
| Tag | `spike-9bad3f4815` (never `latest`) |
| Source | branch commit `9bad3f481587dc932fd134f25e1adbedf0ce6601`, same Dockerfile |
| Build | `az acr run` (ACR Tasks run `cc3`) with a context holding only the Dockerfile's allowlist (`Dockerfile.dockerignore`): the engines' `*.py`, `summary-intro.md`, `requirements.txt`, `entrypoint/*.py`. No other repo file was uploaded. |
| Compressed size | 64.3 MB (12 layers); CI ceiling 400 MB |
| Trivy | `aquasec/trivy:0.67.2`, `--pkg-types os,library --severity HIGH,CRITICAL`, run in the ACR Task against the built image **before** the push step (`push` ran only `when` the gate step succeeded) |
| Trivy gate (`--ignore-unfixed --exit-code 1`) | **pass: 0 fixable HIGH/CRITICAL** (Debian 13.8 layer 0, every Python package 0) |
| Trivy report (unfixed included) | 53 HIGH, 0 CRITICAL, all Debian OS packages in status `affected` (no fixed version); 0 in Python packages |
| CI on the same commit | PR run 46030852360 (pull_request, head `9bad3f48`) green: in-image parity tests and the CI Trivy gate passed |

The build's `apt-get upgrade` layer took the Debian security updates published after the base digest (libzstd1, login.defs, passwd).

## 3. Runs on dev

One run per NDA, each `segment` then `redline` (the engine job's two operations), ids-only Job Contract envelopes hand-sent to `intake-engine-requests` (shape from `entrypoint/envelope.py`: `jobType IntakeEngineRun`, payload `{operation, runId, dispatchAttempt}` only, `idempotencyKey IntakeEngine:{runId}:{operation}:{dispatchAttempt}`). Inputs uploaded to `{runId}/in/source.docx` and `{runId}/in/redline/1/plan.json`. Plans: task 004 run 3 (gpt-5-reasoning, after the D4 fix), from the live test's plan folder, unchanged.

| NDA | runId | Source sha256 | Plan sha256 | Edits |
|---|---|---|---|---|
| nda-01 | `0017c789-56b2-46c8-b79b-1c3789dbd325` | `751165a8…bda10` | `6ce288da…af4b5` | 15 |
| nda-02 | `99ddccce-e7b7-4dd6-bda9-352ba5124879` | `fe2bf0f1…775cf` | `b0a290c1…0f07a` | 15 |
| nda-03 | `081fe420-d0af-4b4b-8511-86d739b8a3b7` | `263c996d…49e73d` | `0cc20246…f8159` | 18 |

Every completion: `outcome completed`; redline `checkOutcome passed`; `refusedEdits []`, `refusedBookmarks []`; `subject IntakeEngineCompleted`, `messageId` = request idempotency key, `correlationId` echoed, `contentType application/json`; payload ids and codes only. One marker per operation (`{runId}/markers/{segment,redline}/1.json`).

## 4. Latency

Timestamps: send (client), execution start (Container Apps execution `startTime`, second resolution), process start (= the job's `JobOutcome` log time minus its `durationMs`, which counts from process start), completion enqueued (Service Bus). KEDA polls every 30 s.

| Execution | Op | Trigger delay (send → execution) | Replica start (execution → process) | In-process (`durationMs`) | End to end (send → completion) |
|---|---|---|---|---|---|
| hwgdc (first after deploy: **cold**) | nda-01 segment | 2.8 s | 49.5 s | 4.4 s | 56.6 s |
| cbnrw | nda-01 redline | 33.7 s | 48.5 s | 5.2 s | 87.3 s |
| cbjvx | nda-02 segment | 15.1 s | 48.7 s | 4.6 s | 68.1 s |
| zgspk | nda-02 redline | 32.0 s | 28.6 s | 4.6 s | 64.9 s |
| wt7kx | nda-03 segment | 17.5 s | 47.2 s | 4.3 s | 68.7 s |
| zbmjn | nda-03 redline | 5.9 s | 46.7 s | 4.6 s | 57.0 s |

Per-NDA run latency (segment + redline end to end, including the client's plan upload between them): nda-01 146.4 s, nda-02 138.0 s, nda-03 128.5 s.

Findings:
- Cold vs warm: on the Consumption profile every execution starts a new replica. The first execution after deploy (49.5 s) is no slower than most later ones (46.7–48.7 s; one 28.6 s). Replica start, not the engines, dominates.
- In-process time is 4.3–5.2 s, of which the engines take about 0.6–0.8 s (local reference: segment 0.11–0.17 s, redline 0.58–0.61 s); the rest is interpreter start-up, MI token, Service Bus and blob round trips.
- Trigger delay is 3–34 s, bounded by the 30 s polling interval.
- Worst end to end 87.3 s, far inside the 600 s job timeout. The escalation trigger (p95 near the timeout) did not fire.

## 5. Golden comparison (dev vs a local run of the same engines and plan)

Local reference: the job's own runner (`entrypoint.engines.run_segment` / `run_redline`, skip-and-report mode) on this branch's engines, Python 3.11.8 on Windows, same corpus file and same plan file.

| NDA | redline-internal.docx | redline-external.docx | clean.docx | clauses.json / check-report.json / refused-report.json | check_outputs |
|---|---|---|---|---|---|
| nda-01 | sha256 equal `031c5591…` | sha256 equal `7d982749…` | sha256 equal `438e028c…` | parsed-equal; bytes equal after CRLF→LF | passed (dev and local) |
| nda-02 | sha256 equal `4d82cd31…` | sha256 equal `def81a17…` | sha256 equal `0bde70f7…` | same | passed |
| nda-03 | sha256 equal `03b8f47e…` | sha256 equal `b786e9c4…` | sha256 equal `772b7785…` | same | passed |

All nine `.docx` outputs are **whole-file byte-identical** between the dev container (Linux) and the local Windows run, so the fixed zip entry dates (#3237) hold across hosts here. The three JSON outputs differ in bytes only by line endings: the local Windows run writes CRLF (text-mode newline translation), dev writes LF; parsed content is equal and the bytes are equal after CRLF→LF. That is a property of the Windows reference host, not a dev divergence (the CI parity run, Linux runner vs image, compares JSON whole-file and passed). Escalation trigger "golden outputs differ" did not fire.

## 6. A2 checks on dev (ADR-004 Amendment A2; FR-01b, FR-01c, FR-01e/f)

| Check | Result |
|---|---|
| Duplicate envelope (nda-01 `redline` attempt 1 re-sent with a new `jobId`, same ids and paths, 16:47:39) | Execution hs8t5 logged `Processing` → `Duplicate` → `JobOutcome` (4.3 s). The run's 10 blobs unchanged (same etags and last-modified); still one marker per operation; no second completion on `intake-engine-completions` (active count 0 afterwards). |
| Malformed envelope (runId `e2fdcb8f-368e-4ecc-a113-b64853788e84`, payload with an extra `blobPath` member, 16:50:15) | Dead-lettered: reason `InvalidFormat`, description `payload_unknown_field`. 0 blobs under that runId. Request queue DLQ count 1 (this message). Left in the DLQ as evidence (14-day TTL). |
| Managed identity only | Job identity `UserAssigned` (`mi-intake-engine-dev` only); registry pull by identity (no username or password secret); KEDA scaler by identity (no connection string); the only Job secret is `appinsights-connection-string`. Staging account has shared-key access disabled, so blob access can only be Entra. |
| Touches only Blob and Service Bus | The identity's whole Azure access is the five grants in section 1 (two queues, the staging container, AcrPull). The container's configuration names only the Service Bus FQDN, the staging blob endpoint and the Application Insights connection string. Known limit carried from task 001: with `APPLICATIONINSIGHTS_CONNECTION_STRING` set, the job also reaches the Application Insights ingestion host (owner to confirm or drop). Network egress was not captured at packet level. |

## 7. Clean copy on nda-02 (Unresolved Q1, resolved by the owner 2026-11-26: Clean accepts all revisions from all sides)

Counts per part, author shown as a hash (dev outputs of run `99ddccce`):

| File | w:ins | w:del | Comment parts | Comment refs |
|---|---|---|---|---|
| Source (counterparty markup) | 1 (author `66c326f9`) | 1 (`66c326f9`) | `word/comments.xml` | 2 |
| clean.docx | **0** | **0** | **none** | **0** |
| redline-internal.docx | 1 (`66c326f9`) + 15 (ours, `734c2625`) | 1 (`66c326f9`) + 14 (ours) | `word/comments.xml` | 30 |
| redline-external.docx | 1 (`66c326f9`) + 15 (ours) | 1 (`66c326f9`) + 14 (ours) | `word/comments.xml` | 2 (the counterparty's) |

The decision holds: the Clean copy has no `w:ins`, `w:del` or comment part; the redline and external outputs keep the counterparty's prior revision pair and comments, and add ours; external drops only our comments. No message to acclerator-r1 was needed.

## 8. Dataverse (dev, `https://spaarkedev1.crm.dynamics.com`)

| Row | Result |
|---|---|
| Action `agreement-edit-plan` | Created by `scripts/ebilling/Ensure-ActionRow.ps1 -ActionCode agreement-edit-plan` (dry run first): `sprk_analysisactionid 22172ab1-82ea-f111-84ea-00224820fd20`, created 2026-11-27T16:50:50Z, `sprk_modeltier` Reasoning (100000002), `sprk_allowsknowledge false`; prompt (10164 chars) and output schema (6210 chars) patched by `Deploy-ActionMirrors.ps1`. Re-run dry: unchanged. |
| Binding (`sprk_playbookconsumer`, consumerType `agreement-edit-plan`) | **Not seeded (held, agreed with the main session).** `RoutingConsumerTypeHealthCheck` treats a Binding row whose consumerType has no BFF constant as drift, and the dev BFF does not carry `ConsumerTypes.AgreementEditPlan` until this branch merges and the gated dev deploy takes master. Order from task 004: BFF, then Action row, then Binding. |

Dev `/healthz/catalog` was already **503 Unhealthy before any change in this task** (reconciliation logged 16:45:50 UTC, before the Action row was created at 16:50:50). Pre-existing drift: 8 Binding rows without a ConsumerTypes constant, plus one tool-row category (live description diverges from the seed, 3 entries). Not investigated here; none is from this project.

The BFF was **not** deployed by this task (owned by the main session's gated coordinator), so the `/bff-deploy` readback criterion is not met here.

## 9. Clean-up done

- Operator spike grants removed (role assignments `89ab6d38-8df7-460c-900c-01c980d56327`, `f3b2d9b7-cc44-4401-92ad-bbd4ef308c32`, `ea1848bd-cbad-4dbc-8fdd-bb3294666f5c`); none of the operator's assignments names an intake scope afterwards.
- The malformed-envelope execution (`spaarke-intake-job-dev-l7qsr`, Succeeded 16:50:35–16:51:08) had not reached Log Analytics when this report was written; the dead-letter reason above is from the queue itself.
- Staging blobs were left for the lifecycle rule (no manual deletes).
- The malformed test message stays in the request queue's DLQ (14-day TTL).

## 10. Open items and follow-ups

1. **Binding row**: run `scripts/dataverse/Seed-PlaybookConsumers.ps1` for the `agreement-edit-plan` row right after the dev BFF deploy that carries `ConsumerTypes.AgreementEditPlan` (lands after the branch merges to master). Then check `/healthz/catalog` shows no new drift entry for it.
2. **BFF app setting** `ServiceBus__IntakeEngineCompletionQueueName=intake-engine-completions` on `spaarke-bff-dev`: not set (nothing consumes it until task 014). Set it with the task 014 deploy.
3. **BFF deploy** (tasks 004–006 content) via `/bff-deploy` and its readback: main session / gated coordinator, after merge.
4. **CI-published image**: after merge, the master push publishes the image and `provisioning-artifacts/intake-engine/latest.json`; redeploy `dev-intake-engine.bicep` step 3 with that digest (what-if first) to replace the spike image.
5. **Shared lifecycle policy** on `sprkspaarkedevebillsa`: tell ebilling-r1 that the account now has a `managementPolicies/default` owned by the intake rule; any eBilling lifecycle rule must be merged into it.
6. **Replica start ≈ 47–50 s** per execution on Consumption is the main latency; a run (segment + redline) is about 2.2–2.5 minutes end to end. Acceptable for r1 (asynchronous, attorney-reviewed); if task 014 or the UX needs faster, options are a single execution doing both operations, a shorter polling interval, or a dedicated workload profile.
7. Pre-existing dev catalog drift (section 8) belongs to its owners; not filed by this task.
8. `#3210` (customer-provisioning-orchestration-r1): the three-step flow worked as designed; differences are listed in section 1.
