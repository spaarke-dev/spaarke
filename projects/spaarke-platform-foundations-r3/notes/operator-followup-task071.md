# Operator Follow-Up — Task 071 (Service Bus Membership Topic)

> **Task**: `projects/spaarke-platform-foundations-r3/tasks/071-provision-service-bus-topic.poml`
> **Status as of authoring**: Bicep authored + compiled clean; **deployment deferred to operator**
> **Acceptance criterion**: AC-1P2.2 (spec.md) — "Service Bus topic `sprk-membership-changes` + subscription `recon-junction-updater` provisioned. *Verify*: Azure CLI / Bicep diff."
> **D3 decision** (resolved 2026-06-20, spec.md FR-2P2.3): Topic + subscription-per-consumer; NOT a queue; NOT reusing `ServiceBusJobProcessor` / `sdap-jobs` queue.

---

## What was authored

### Bicep changes (all in `infrastructure/bicep/`)

1. **NEW** `modules/membership-topic.bicep` — self-contained module:
   - Topic `sprk-membership-changes` (1 GB max, 14-day TTL, non-partitioned, batched ops)
   - Subscription `recon-junction-updater` (10 max delivery, 5-min lock, 14-day TTL, DLQ-on-expiration)
   - Conditional RBAC (`if (!empty(bffPrincipalId))`):
     - **Azure Service Bus Data Sender** (`69a216fc-b8fb-44d8-bc22-1f3c2cd27a39`) on the **topic**
     - **Azure Service Bus Data Receiver** (`4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0`) on the **subscription**

2. **MODIFIED** `customer.bicep`:
   - Added param `bffPrincipalId` (default `''`)
   - Added `membershipTopic` module wiring (depends on `serviceBus` module)
   - Added outputs `membershipTopicName` + `membershipReconSubscriptionName`

3. **MODIFIED** `stacks/model2-full.bicep`:
   - Added `membershipTopic` module after `kvRbacAppService` (uses `bffApi.outputs.appServicePrincipalId` directly since BFF + SB co-located in same RG in Model 2)
   - Added outputs `membershipTopicName` + `membershipReconSubscriptionName`

### Bicep validation

```
az bicep build --file infrastructure/bicep/modules/membership-topic.bicep --outdir <out>
az bicep build --file infrastructure/bicep/customer.bicep --outdir <out>
az bicep build --file infrastructure/bicep/stacks/model2-full.bicep --outdir <out>
```

All three compile cleanly. The BCP318 warnings in `stacks/model2-full.bicep` are pre-existing (unrelated to this change) — they apply to `vnet` module references inside `if (enableVnet)` blocks, not to the new `membershipTopic` module.

---

## Why this is NOT auto-deployed

Per task constraints (CLAUDE.md §10 + spec.md NFR-01 + general infra-as-code hygiene): infrastructure changes are deployed through the existing pipeline / operator runbook, not autonomously by Claude Code. This task's deliverable is the **Bicep artifact + validation**; deployment is the operator's gate.

---

## ✅ RESOLVED 2026-06-23 — Option A taken; deploy succeeded

**Decision**: Owner approved Option A (upgrade dev `spaarke-servicebus-dev` Basic→Standard) based on cross-project analysis showing R3 + Insights Engine + future cross-cutting events all need topic support; consolidating to one Standard namespace is cleaner than multiple namespaces or deferring Phase 2 work.

**Actions taken**:

1. **Namespace upgrade** (2026-06-23):
   ```bash
   az servicebus namespace update \
     --resource-group SharePointEmbedded \
     --name spaarke-servicebus-dev \
     --set sku.name=Standard sku.tier=Standard
   ```
   Result: Basic → Standard, in-place, all 7 existing queues preserved (`document-events`, `office-indexing`, `office-jobs`, `office-profile`, `office-upload-finalization`, `sdap-communication`, `sdap-jobs`). Active SB traffic on `sdap-jobs` continued through the upgrade without disruption.

2. **Topic + subscription + RBAC deploy** (2026-06-23):
   ```bash
   az deployment group create \
     --resource-group SharePointEmbedded \
     --template-file infrastructure/bicep/modules/membership-topic.bicep \
     --parameters serviceBusNamespaceName=spaarke-servicebus-dev \
                  bffPrincipalId=9fd47efb-7962-492b-ac44-e5ccd0268ebb
   ```
   Result: ✅ `provisioningState=Succeeded`. 4 resources created.

**Verified post-deploy**:
- Topic `sprk-membership-changes` — Active, 1024 MB, P14D TTL
- Subscription `recon-junction-updater` — Active, 10 max delivery, PT5M lock
- BFF MI (`9fd47efb-7962-492b-ac44-e5ccd0268ebb`) → `Azure Service Bus Data Sender` on topic
- BFF MI → `Azure Service Bus Data Receiver` on subscription

**Status of task 071**: ✅ COMPLETE for dev (was ❌ `blocked-operator`).

**Next gates**:
- Task 073 (topic/subscription smoke test) — ready to run against dev
- BFF deploy — still paused per team coordination (user directive 2026-06-22)
- UAT/staging/prod — separate deploys; their SB namespaces (Standard already) can take the topic Bicep directly

---

## Original blocker analysis (preserved for historical record)

## ⚠️ BLOCKER DISCOVERED 2026-06-22 (deploy attempt)

A `what-if` + `create` deploy was attempted against the dev environment on **2026-06-22** and **failed** with:

```
SubCode=40000. Cannot operate on type Topic because the namespace
'spaarke-servicebus-dev' is using 'Basic' tier.
```

### Current Service Bus tier inventory (subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`)

| Namespace | RG | Tier | Topics supported? |
|---|---|---|---|
| `spaarke-servicebus-dev` | `SharePointEmbedded` | **Basic** | ❌ No |
| `spaarke-demo-prod-sbus` | `rg-spaarke-demo-prod` | Standard | ✅ Yes |

### Decision Required (coordinate with team before proceeding)

The dev environment cannot host the `sprk-membership-changes` topic until ONE of:

**Option A — Upgrade `spaarke-servicebus-dev` Basic → Standard**
- One-way upgrade (per Azure docs — can't downgrade afterwards)
- Existing `sdap-jobs` queue keeps working (queue is supported on both tiers)
- Cost delta: Basic (~$0.05/M ops) → Standard ($10/mo base + per-op)
- Blast radius: shared dev infra; affects all consumers of this namespace
- Pros: simplest; one namespace for all dev SB workload (queues + topics)
- Cons: irreversible; cost increase; would need budget/governance sign-off

**Option B — Create a new dedicated Standard namespace for topics**
- E.g., `spaarke-sbtopics-dev` in a R3-owned RG
- Leaves existing Basic namespace untouched for queue consumers
- Operator runbook (this file) + Bicep `serviceBusNamespaceName` param changes to point at the new namespace
- BFF App Service config `Membership:JunctionUpdater:ServiceBusNamespace` likewise points at the new namespace
- Pros: zero impact on existing queue consumers; recoverable (just delete the new namespace to revert)
- Cons: 2 SB namespaces to operate in dev; minor cognitive overhead

**Option C — Defer Phase 2 in dev**
- Keep all 3 R3 kill-switches (`Membership:EventPublisher`, `JunctionUpdater`, `CacheInvalidator`) at `Enabled=false`
- Phase 1A (membership endpoints + admin endpoints + nightly recon) works without the topic
- Run UAT against Phase 1A only in dev; deploy Phase 2 directly to UAT/staging where the SB namespace is Standard
- Pros: zero infra change in dev; preserves the team-coordination pause; testing path is clear
- Cons: real-time event flow is never exercised in dev; recon-only operation is the only freshness mechanism

**Recommendation**: **Option C for immediate UAT** + **Option B for medium-term** (gives Phase 2 a verifiable dev surface without the irreversibility of Option A). Owner makes the call.

### Status of this task pending decision
- Bicep artifact: ✅ authored + `az bicep build` clean + `az deployment group what-if` shows 4 resources to create with no destructive changes
- Bicep deploy: ❌ FAILED on dev tier mismatch; not attempted on other envs
- BFF code: ✅ ready (publisher + handler + invalidator with ADR-032 Null peers default OFF; recon job ENABLED by default since it's topic-independent)
- Operator follow-up: pick Option A / B / C; coordinate with shared-dev consumers; re-run this runbook section "Operator deployment steps" against the right namespace

---

## Operator deployment steps

### Prerequisites
- Confirm target environment (dev / staging / prod) + customer ID (Model 1/2) OR shared platform (Model 2 full).
- Get the BFF App Service Managed Identity principal ID for the target environment:
  ```bash
  az webapp identity show \
    --resource-group rg-spaarke-platform-{env} \
    --name spaarke-bff-{env} \
    --query principalId -o tsv
  ```
- Have `az` ≥ 2.50 with Bicep ≥ 0.30 installed (current build uses Bicep 0.30.x; warning suggests 0.44.1 is available — upgrade is optional).

### Customer-RG path (Model 1 — customer.bicep)

```bash
# Replace <env>, <customerId>, <bffMiPrincipalId> with actual values
az deployment sub create \
  --location westus2 \
  --template-file infrastructure/bicep/customer.bicep \
  --parameters customerId=<customerId> \
               environmentName=<env> \
               bffPrincipalId=<bffMiPrincipalId>
```

Note: this re-runs the FULL customer.bicep. If you want ONLY the topic/subscription/RBAC (additive, lower blast radius), wrap the new module in a small standalone template that targets the existing customer RG. Recommended for incremental rollout — see "Standalone deployment" below.

### Full-stack path (Model 2 — stacks/model2-full.bicep)

```bash
az deployment sub create \
  --location westus2 \
  --template-file infrastructure/bicep/stacks/model2-full.bicep \
  --parameters @infrastructure/bicep/stacks/parameters/model2-<env>.bicepparam
```

`bffApi.outputs.appServicePrincipalId` is wired automatically — no extra parameter needed.

### Standalone deployment (additive — recommended for incremental rollout)

If the customer / platform stack is already deployed and you want ONLY to add the topic + subscription + RBAC, target the existing customer RG directly:

```bash
# 1. Get BFF MI principal ID
BFF_MI=$(az webapp identity show \
  --resource-group rg-spaarke-platform-<env> \
  --name spaarke-bff-<env> \
  --query principalId -o tsv)

# 2. Deploy ONLY the new module against the existing customer RG (where the SB namespace lives)
az deployment group create \
  --resource-group rg-spaarke-<customerId>-<env> \
  --template-file infrastructure/bicep/modules/membership-topic.bicep \
  --parameters serviceBusNamespaceName=spaarke-<customerId>-<env>-sbus \
               bffPrincipalId=$BFF_MI
```

Replace `spaarke-<customerId>-<env>-sbus` with the actual SB namespace name (see `customer.bicep` line ~94 — naming convention is `spaarke-{customerId}-{env}-sbus`).

### Pre-deploy dry run (recommended)

```bash
# Run what-if against any of the above commands to preview changes without applying
az deployment {sub|group} what-if \
  --... (same args as the create command)
```

---

## Verification (post-deploy)

1. **Azure portal**: navigate to the SB namespace → Topics → confirm `sprk-membership-changes` exists with 1 subscription `recon-junction-updater`.

2. **CLI**:
   ```bash
   # Topic exists
   az servicebus topic show \
     --resource-group rg-spaarke-<customerId>-<env> \
     --namespace-name spaarke-<customerId>-<env>-sbus \
     --name sprk-membership-changes

   # Subscription exists
   az servicebus topic subscription show \
     --resource-group rg-spaarke-<customerId>-<env> \
     --namespace-name spaarke-<customerId>-<env>-sbus \
     --topic-name sprk-membership-changes \
     --name recon-junction-updater
   ```

3. **RBAC** (BFF MI role assignments):
   ```bash
   # Sender on topic
   az role assignment list \
     --assignee $BFF_MI \
     --scope $(az servicebus topic show --resource-group rg-spaarke-<customerId>-<env> \
                  --namespace-name spaarke-<customerId>-<env>-sbus \
                  --name sprk-membership-changes --query id -o tsv)

   # Receiver on subscription
   az role assignment list \
     --assignee $BFF_MI \
     --scope $(az servicebus topic subscription show \
                  --resource-group rg-spaarke-<customerId>-<env> \
                  --namespace-name spaarke-<customerId>-<env>-sbus \
                  --topic-name sprk-membership-changes \
                  --name recon-junction-updater --query id -o tsv)
   ```

4. **Smoke send/receive** — performed by task 073 (Bicep deploy + topic/subscription smoke test). Tasks 072 (payload contract) and 084 (handler) are downstream consumers of this provisioning.

---

## Follow-up tasks (downstream)

| Task | Purpose | Depends on |
|---|---|---|
| 072 | `MembershipChangedEvent` payload contract | 070 + **071** |
| 073 | Bicep deploy + topic/subscription smoke test | **071** + 072 |
| 081–083 | Wire event-publishing into matter / document / event / task / opportunity mutation endpoints | 080 (P-event-1) + **071** |
| 084 | `MembershipJunctionUpdater` handler (subscription consumer) | **071** + 072 |

---

## Open items / risks

- **Cross-RG principal-ID flow**: In Model 1 deployments, the BFF lives in `rg-spaarke-platform-{env}` and the SB namespace lives in `rg-spaarke-{customerId}-{env}`. Operator MUST pass `bffPrincipalId` explicitly when re-running `customer.bicep`. The default empty value will silently skip RBAC (matches the cosmos-db.bicep + key-vault.bicep pattern). **Mitigation**: the standalone deployment recipe above wires this in a single step.
- **D3 namespace placement** (review opportunity): the topic currently rides the per-customer SB namespace (`spaarke-{customerId}-{env}-sbus`). If R4 wants a shared platform-level SB namespace for cross-tenant fan-out, this module can be re-pointed by changing `serviceBusNamespaceName`. No code in this PR locks in the per-customer placement; it's just where the existing namespace lives today.
- **Bicep version**: Current `az` ships Bicep 0.30.x; warning suggests upgrading to 0.44.1. Not required for this PR — the module uses no syntax features beyond 0.20.
