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
