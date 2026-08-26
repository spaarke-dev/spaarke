# Runbook — `sprk-provisioning-jobs` queue delete + recreate (sessions + dedup)

> **Task**: 108 (`Bicep: recreate sprk-provisioning-jobs queue with sessions + dedup (C5.4/C4.6) + drain-verify runbook`)
> **Authored**: 2026-08-19, Phase C'' Wave G-1
> **Status of this document**: AUTHORING ONLY. No destructive command in this runbook has been executed by task 108. Live execution is a separate, human-run ceremony (see "Who runs this" below).
> **References**: DS-5 §C4.6 + §C5.4 (`notes/design-study-ds5-cat456-remediation.md`), DS-2/DS-2b session-serialized dispatch decision (`notes/design-study-ds2-dispatcher-design.md`), spec.md FR-22, task 107 (`ReconcilerEnqueuePayload` attempt field), task 102 (`ServiceBusSessionProcessor`).

---

## 1. Why this is needed

`requiresDuplicateDetection` and `requiresSession` are **create-time-only** properties on an Azure Service Bus queue — they cannot be applied to an existing queue via an in-place ARM/Bicep deployment. The live `sprk-provisioning-jobs` queue (namespace `spaarke-servicebus-dev`, resource group `SharePointEmbedded`) was created via bare `az servicebus queue create` defaults: **both OFF**.

Task 108 lands the desired end state in Bicep (`infrastructure/bicep/modules/controlplane-sb-queue.bicep`, invoked from `infrastructure/bicep/platform-controlplane.bicep`), but **landing the Bicep declaration does not change the live queue**. Empirical proof (see §2) — an in-place `az deployment` attempt against the live queue is evaluated as a `Modify`, not a `Create`, and Azure Service Bus will not honor the `requiresSession` / `requiresDuplicateDetection` deltas on an existing queue. Only delete + recreate achieves the desired live state.

**Consumers that depend on this landing**:
- Task 102's `ServiceBusSessionProcessor` throws immediately at `StartProcessingAsync` if pointed at a non-session queue — a session-aware receiver requires `requiresSession: true` on the live queue.
- FR-22 Level-1 (wire-level) idempotency stays permanently inert until `requiresDuplicateDetection: true` is live.

---

## 2. Empirical confirmation (non-mutating `what-if`, run 2026-08-19)

Ran a resource-group-scoped `what-if` directly against the new module (isolated from the rest of the L2 stamp's known config-key drift — see §6):

```bash
az deployment group what-if \
  --resource-group SharePointEmbedded \
  --template-file infrastructure/bicep/modules/controlplane-sb-queue.bicep \
  --parameters serviceBusNamespaceName=spaarke-servicebus-dev
```

Result — `changeType: Modify` against the existing queue, with this delta:

```
properties.deadLetteringOnMessageExpiration   Modify   False -> True
properties.duplicateDetectionHistoryTimeWindow Modify  PT10M -> PT1H
properties.requiresDuplicateDetection          Modify  False -> True
properties.requiresSession                     Modify  False -> True
properties.autoDeleteOnIdle                    Delete  (default) -> None
properties.defaultMessageTimeToLive            Delete  (default) -> None
properties.enablePartitioning                  Delete  False -> None
properties.maxMessageSizeInKilobytes           Delete  256 -> None
properties.maxSizeInMegabytes                  Delete  1024 -> None
```

This confirms the create-time-only constraint behaviorally, not just per Microsoft Learn documentation: an `az deployment group create` against this template would attempt to modify a live, existing queue in place. **Do not run `az deployment group create` (or `az deployment sub create`) against the live queue as a substitute for delete+recreate** — Azure Service Bus will silently retain the old `requiresSession`/`requiresDuplicateDetection` values (the two properties that matter most here) while still landing the crisper deltas as a side effect (`deadLetteringOnMessageExpiration`, `duplicateDetectionHistoryTimeWindow`), producing a queue in a partially-migrated, non-obvious state. Delete + recreate is the only correct path.

---

## 3. Live state snapshot (read-only checks, 2026-08-19)

Current live queue properties (`az servicebus queue show --name sprk-provisioning-jobs --namespace-name spaarke-servicebus-dev --resource-group SharePointEmbedded`):

| Property | Live value today | Desired (post-recreate) |
|---|---|---|
| `requiresSession` | `false` | `true` |
| `requiresDuplicateDetection` | `false` | `true` |
| `duplicateDetectionHistoryTimeWindow` | `PT10M` (Azure default) | `PT1H` |
| `lockDuration` | `PT5M` | `PT5M` (unchanged) |
| `maxDeliveryCount` | `10` | `10` (unchanged) |
| `deadLetteringOnMessageExpiration` | `false` | `true` |
| `activeMessageCount` | **0** | — |
| `deadLetterMessageCount` | **0** | — |

**This supersedes the task-authoring-time assumption of "1 stale H0 envelope" from the C4.5-deleted dead test run.** As of this runbook's authoring, the live queue has **zero** active or dead-lettered messages — the message referenced in `current-task.md` / DS-5 must have already expired (Azure default `defaultMessageTimeToLive`) or been drained during the 2026-08-18 L2 fix/redeploy cycle. The delete-and-recreate window is therefore **even safer today than originally assessed** (zero in-flight message loss risk), but the pre-check step below (§4 step 1) MUST be re-run immediately before deletion — this snapshot will go stale the moment any new `/api/runs` POST or H0.5 consent-callback lands on the live L2 stamp.

**RBAC baseline** (`az role assignment list --scope <namespace-resource-id>`, MSYS_NO_PATHCONV=1 needed on Git Bash — the leading `/subscriptions/...` scope string is otherwise mangled by MSYS path translation):

| Principal | Role | Scope |
|---|---|---|
| `38f7693f-e6e2-4a3e-9acf-7f9e29dd4044` (`sprk-controlplane-dev-uami`) | Azure Service Bus Data Sender | **namespace** (`.../namespaces/spaarke-servicebus-dev`) |

This grant is **namespace-scoped**, not queue-scoped — Azure RBAC role assignments at a parent scope are unaffected by a child resource's (the queue's) delete/recreate lifecycle by construction, not just by convention. Zero queue-scope role assignments exist today (confirmed: `az role assignment list --scope <namespace-id>/queues/sprk-provisioning-jobs` returns empty), so there is nothing queue-scoped to lose. **No re-grant is needed after this runbook's delete+recreate step.** Note: "Azure Service Bus Data Receiver" is **not yet granted anywhere** — that is task 110's scope (C5.5), a separate hard prerequisite for task 102's dispatcher to receive any message; it is independent of this runbook.

---

## 4. Runbook steps (LIVE — run by the operator, NOT by this task)

**Preconditions before running this section:**
- Task 108's Bicep (this task) has landed and `az bicep build` + the module-scoped `what-if` above have been re-verified against current source.
- Task 109 (config-key/audience drift fix) does NOT need to land first for this runbook specifically — the queue recreate is isolated to `modules/controlplane-sb-queue.bicep`, deployed standalone at the namespace's resource-group scope (see step 3 below), NOT via the full `platform-controlplane.bicep` subscription-scope stack. Deploying the full stack today would also re-apply the OTHER known Bicep-vs-live config drift (DS-5 C5.1) that task 109 has not yet fixed, and would wipe live manual app-setting aliases per DS-5's ordering rule — avoid that by deploying only the queue module, not the whole stack.
- **Strongly recommended**: sequence the LIVE execution of this runbook to happen only after task 107 (`ReconcilerEnqueuePayload` attempt field) has shipped to the L2 code — see §5 below for why.

### Step 1 — Pre-check (re-verify message count immediately before deleting)

```bash
az servicebus queue show \
  --name sprk-provisioning-jobs \
  --namespace-name spaarke-servicebus-dev \
  --resource-group SharePointEmbedded \
  --query "countDetails"
```

Expected (per §3 snapshot): `activeMessageCount: 0`, `deadLetterMessageCount: 0`. **Escalation trigger (per task 108 POML)**: if this shows MORE than the 1 originally-expected stale message (i.e., any unexpected live traffic), STOP before deleting — escalate per root CLAUDE.md §6 rather than discarding unknown in-flight work. A count of 0 or 1 (the known stale envelope, if it has NOT yet expired by the time this runs) is safe to proceed.

### Step 2 — Delete the live queue

```bash
az servicebus queue delete \
  --name sprk-provisioning-jobs \
  --namespace-name spaarke-servicebus-dev \
  --resource-group SharePointEmbedded
```

This is destructive and irreversible for any message still in the queue at delete time — which is why step 1 must be re-run immediately beforehand, not relied upon from this document's authoring-time snapshot.

### Step 3 — Deploy the Bicep-declared queue

Deploy **only the queue module**, scoped directly to the namespace's resource group (narrower blast radius than the full stack — see preconditions above):

```bash
az deployment group create \
  --resource-group SharePointEmbedded \
  --template-file infrastructure/bicep/modules/controlplane-sb-queue.bicep \
  --parameters serviceBusNamespaceName=spaarke-servicebus-dev
```

(Alternative, once task 109 has landed and a full-stack redeploy is otherwise being performed for other reasons: `az deployment sub create --template-file infrastructure/bicep/platform-controlplane.bicep --parameters environmentName=dev ...` — this also lands the queue as part of the complete stack. Not required for this runbook in isolation.)

### Step 4 — Verify

```bash
az servicebus queue show \
  --name sprk-provisioning-jobs \
  --namespace-name spaarke-servicebus-dev \
  --resource-group SharePointEmbedded \
  --query "{requiresSession:requiresSession, requiresDuplicateDetection:requiresDuplicateDetection, duplicateDetectionHistoryTimeWindow:duplicateDetectionHistoryTimeWindow}"
```

Expected: `requiresSession: true`, `requiresDuplicateDetection: true`, `duplicateDetectionHistoryTimeWindow: "PT1H"`.

### Step 5 — RBAC survival check

```bash
MSYS_NO_PATHCONV=1 az role assignment list \
  --scope "/subscriptions/<sub-id>/resourceGroups/SharePointEmbedded/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev" \
  --query "[].{principal:principalId, role:roleDefinitionName}"
```

Expected: identical to the §3 baseline (`sprk-controlplane-dev-uami` → Azure Service Bus Data Sender, namespace scope) — no re-grant needed. If this list is EMPTY post-recreate, something unexpected happened (namespace-scope RBAC should be architecturally immune to a child queue's lifecycle) — escalate rather than assume drift is benign.

---

## 5. PT1H dedup window vs §4C retry semantics — READ BEFORE FLIPPING DEDUP ON LIVE

Turning on `requiresDuplicateDetection: true` (this runbook) **without** task 107's fix creates a silent failure mode in the §4C `RetryableWithCleanup` auto-retry path:

- `StateReconcilerService.ApplyHandlerOutcomeAsync` re-enqueues a failed handler via `BuildEnvelope`, whose `ReconcilerEnqueuePayload` is deliberately byte-stable (`EnqueuedAt` is not in the hash) — this byte-stability exists on purpose, to suppress duplicate enqueues across reconciler ticks for the *first-enqueue* path.
- Once dedup is live, that SAME byte-stability means a §4C retry re-enqueue for the SAME `HandlerId|RunId|CustomerId|paramHash` produces the **IDENTICAL** `MessageId` as the original, just-consumed dispatch.
- Azure Service Bus's own duplicate-detection window (now `PT1H`) silently drops the retry message — it never reaches the queue as a new delivery. The operator sees a run stuck at the failed phase with **no visible re-enqueue, no error, no signal** that the retry never happened.
- **Task 107** (`Add attempt field to ReconcilerEnqueuePayload`) is the fix: `MessageId = SHA256(HandlerId|RunId|CustomerId|paramHash|attempt)`, where `attempt` increments only on the reconciler's re-enqueue path (first-enqueue stays `attempt`-absent/zero, preserving the tick-duplicate-suppression this byte-stability exists for).

**Operational implication for this runbook**: task 107 is scheduled as a dependent of task 108 in `TASK-INDEX.md` (`107.deps = 108`) — meaning 107's *code change* lands after 108's Bicep, but 107's code change does NOT itself depend on 108's *live* delete+recreate (its POML explicitly notes the code change is independently authorable/testable against a local dedup simulation). **Recommendation: land task 107's code to the L2 App Service BEFORE running §4 of this runbook against the live queue.** If the live queue is recreated with dedup ON while task 107's fix has not yet been deployed, any §4C retry that occurs within the PT1H window after a handler's original failed attempt will be silently swallowed until 107 ships. This is a live-operations sequencing note, not a Bicep concern — the Bicep declaration (this task) is correct and safe to land in git immediately; it is the **live §4 execution** that should wait on 107's deploy.

If an operator must run §4 before 107 has shipped (e.g., to unblock task 102's session-processor smoke test independently of retry-path testing), that is an acceptable narrower use — just do not exercise or rely on §4C auto-retry behavior in that window, and flag any run that appears "stuck after a failure with no re-enqueue" as an expected symptom of this gap, not a new bug.

---

## 6. Repeatability for prod / other stamps

This runbook is written against the **dev** stamp (`spaarke-servicebus-dev` / `SharePointEmbedded` resource group — a legacy, pre-per-env-model resource group name; see `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` "Dev Environment (DO NOT RENAME)"). For staging/prod:

1. Confirm the fleet Service Bus namespace name and its resource group for that environment (NOT assumed to follow `spaarke-servicebus-{env}` / `SharePointEmbedded` — those are dev's legacy values; `infrastructure/bicep/platform-controlplane.bicep`'s `serviceBusNamespaceName` / `serviceBusResourceGroupName` parameters must be passed explicitly once those environments' shared Service Bus resource group naming is decided).
2. Re-run §1 (pre-check) and §2 (empirical `what-if` confirmation) against that environment's live queue BEFORE assuming the same "0 messages, safe today" conclusion — prod will very likely NOT have a safe zero-message window; **prod requires a coordinated drain, not an ad-hoc delete** (per DS-5 C4.6's own framing: "a one-time safe window; document the runbook so a FUTURE queue-property change (post-go-live) is a coordinated drain, not an ad-hoc delete").
3. Steps 2–5 (delete/deploy/verify/RBAC-check) are otherwise identical, substituting the environment's namespace name + resource group name in every command.
4. Re-verify §5's task-107 sequencing recommendation applies equally — do not flip dedup on in prod before task 107's code is live in that environment's L2 deployment.

---

## 7. Who runs this

This runbook is authored by task 108 (Bicep + drain-verify documentation). Per the task's explicit scope boundary, **task 108 does NOT execute §4 of this runbook against live Azure** — that is a separate, human-run (or explicitly separately-dispatched) ceremony, sequenced per §5's recommendation (after task 107 lands) and coordinated with task 102 (the session-processor consumer that requires this queue's `requiresSession: true` to function at all).
