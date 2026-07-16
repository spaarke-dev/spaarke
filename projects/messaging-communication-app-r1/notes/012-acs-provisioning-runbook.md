# 012 — ACS + Event Grid Provisioning: Runbook + Data-Residency Rationale

> **Task**: `012-acs-provisioning-eventgrid` · **Rigor**: FULL (BFF-touching) · **Date**: 2026-07-16
> **FR**: FR-18 · **ADRs**: ADR-027 (per-customer isolation), ADR-028 (central credential), ADR-010/032, ADR-046 (input)
> **Blocks**: 030 (Event Grid webhook ingress consumes the subscription this task creates)
> **Live provisioning status**: **DEFERRED** (no live Azure subscription/ACS available this window — see §4)

---

## 1. What this task shipped

Two-part **extension of the ADR-027 per-customer provisioning orchestrator** (NOT a parallel path):

1. **Bicep (the real per-boundary resource provisioning)** — extends `infrastructure/bicep/customer.bicep`
   (the ADR-027 per-customer orchestrator) with a new `modules/acs-communication.bicep`, mirroring the
   existing `membership-topic` module. Provisions, **per customer boundary**:
   - a **per-boundary ACS resource** (`Microsoft.Communication/communicationServices`) with the
     **immutable** `dataLocation` chosen at create (D-01 residency mechanism);
   - an **Event Grid system topic** on the ACS resource;
   - an **event subscription** delivering the chat events to the **BFF webhook** (task 030 ingress),
     with a **dead-letter Storage** destination configured **from day one** (§8.3) — reusing the
     existing customer Storage account (a `acs-eventgrid-deadletter` container).
   - Gated by `deployAcsMessaging` (default `false`) so existing customer provisioning is unchanged
     until messaging is enabled for a boundary.
   - **BFF publish-size delta: ≈0** — no in-process ARM management SDK added (Bicep/operator-driven).

2. **BFF-side provisioning helper (thin, testable)** — `Services/Registration/CommunicationProvisioning/`:
   - `AcsProvisioningOptions` (+ `AcsEventGridOptions`, `AcsDeadLetterOptions`, `AcsChatEventTypes`)
     — config surface: data location, webhook URL, the 5 chat event types, dead-letter Storage.
   - `AcsBoundaryProvisioningService` — builds an `AcsProvisioningPlan` per boundary (per-boundary
     immutable data location; chat events → webhook + dead-letter) and hands it to the seam.
   - `IAcsBoundaryProvisioner` + `DeferredAcsBoundaryProvisioner` (ADR-032 Null-Object) — R1 registers
     the deferred provisioner (records intent, returns `Deferred`); injects the central
     `TokenCredential` (ADR-028/NFR-05) that a future live impl would authenticate with.
   - Registered **unconditionally** in `RegistrationModule` (ADR-032). See §5 for the DI-ownership note.

---

## 2. Data-residency rationale (ADR-046 input — task 007 owns the ADR body)

> Pointer for ADR-046 (do **not** edit `.claude/adr/` from this task — root §3 write boundary).

**ACS data location is IMMUTABLE at create time** (design §8.7, decision D-01). It cannot be changed
after the resource is created. Therefore **data residency is achieved by provisioning a separate ACS
resource per customer boundary**, with the `dataLocation` parameter chosen deliberately at onboarding.
This is exactly the per-customer isolation concern **ADR-027** already governs, so messaging **extends**
that orchestrator (`customer.bicep`) rather than forking a second provisioning/isolation mechanism.

Consequence for operators: **choosing the wrong data location is not correctable in place** — it
requires deleting and recreating the ACS resource (and re-wiring Event Grid). Confirm the boundary's
required residency **before** running the provisioning.

---

## 3. Config keys (BFF `AcsProvisioning` section + Bicep params)

| BFF option (`AcsProvisioning:*`) | Bicep param (`customer.bicep`) | Purpose |
|---|---|---|
| `DataLocation` | `acsDataLocation` | **Immutable** ACS data location per boundary (D-01). e.g. `UnitedStates`, `Europe`, `Australia`, `UK`. |
| `ResourceLocation` | (module `resourceLocation`, `global`) | Control-plane region for ACS + system topic (always `global`). |
| `EventGrid:WebhookEndpointUrl` | `acsWebhookEndpointUrl` | BFF inbound webhook (task 030 ingress) the subscription delivers to. |
| `EventGrid:IncludedEventTypes` | (module `includedEventTypes`) | The 5 chat events (see `AcsChatEventTypes.Default`). |
| `EventGrid:DeadLetter:StorageAccountResourceId` | (module wires customer Storage `storageAccountId`) | Dead-letter Storage account. |
| `EventGrid:DeadLetter:ContainerName` | `acsDeadLetterContainerName` (`acs-eventgrid-deadletter`) | Dead-letter blob container. |
| — | `deployAcsMessaging` (default `false`) | Per-boundary opt-in for the ACS module. |

The 5 chat event types (must match Bicep `includedEventTypes`):
`Microsoft.Communication.ChatMessageReceivedInThread`, `...ChatMessageEditedInThread`,
`...ChatMessageDeletedInThread`, `...ParticipantAddedToThread`, `...ParticipantRemovedFromThread`.

---

## 4. Operator runbook — live provisioning (DEFERRED; run against a real subscription)

No live Azure subscription/ACS was provisionable this window; the code + Bicep are built and unit-tested
with the live steps captured here. Two paths — **prefer Bicep** (declarative, per-boundary, ADR-027):

### 4A. Bicep (recommended — extends the per-customer orchestrator)

```bash
# From infrastructure/bicep — deploy/extend a customer boundary with messaging enabled.
az deployment sub create \
  --location westus2 \
  --template-file customer.bicep \
  --parameters \
      customerId=acme environmentName=prod \
      deployAcsMessaging=true \
      acsDataLocation=UnitedStates \
      acsWebhookEndpointUrl=https://<bff-host>/api/communications/acs/eventgrid \
      bffPrincipalId=<bff-app-service-mi-object-id>
```
This creates `sprk-acme-prod-acs` + system topic `sprk-acme-prod-acs-egt` + subscription
`chat-events-to-bff` (→ webhook, dead-letter to the customer Storage `acs-eventgrid-deadletter`).

### 4B. Imperative `az` (equivalent; from the task-003 spike §7)

```bash
# 1. ACS resource (data location IMMUTABLE — choose deliberately, D-01)
az communication create --name sprk-acme-prod-acs --resource-group rg-spaarke-acme-prod \
   --location global --data-location UnitedStates

# 2. Event Grid system topic on the ACS resource
az eventgrid system-topic create --name sprk-acme-prod-acs-egt --resource-group rg-spaarke-acme-prod \
   --source $(az communication show -n sprk-acme-prod-acs -g rg-spaarke-acme-prod --query id -o tsv) \
   --topic-type Microsoft.Communication.CommunicationServices --location global

# 3. Subscription: chat events -> BFF webhook + dead-letter (from day one, §8.3)
az eventgrid system-topic event-subscription create --name chat-events-to-bff \
   --resource-group rg-spaarke-acme-prod --system-topic-name sprk-acme-prod-acs-egt \
   --endpoint https://<bff-host>/api/communications/acs/eventgrid \
   --included-event-types \
      Microsoft.Communication.ChatMessageReceivedInThread \
      Microsoft.Communication.ChatMessageEditedInThread \
      Microsoft.Communication.ChatMessageDeletedInThread \
      Microsoft.Communication.ParticipantAddedToThread \
      Microsoft.Communication.ParticipantRemovedFromThread \
   --deadletter-endpoint <customer-storage-resource-id>/blobServices/default/containers/acs-eventgrid-deadletter
```

### 4C. Auth (ADR-028)
Grant the BFF App Service Managed Identity the **Communication and Email Service Owner** role on the
ACS resource so server-side identity/token/thread operations (tasks 010/011/020) authenticate via
`DefaultAzureCredential` (the DI-registered `TokenCredential`). **No connection string / access key in
Azure** — access-key path is local-dev only.

---

## 5. Deviation from POML (documented) — DI ownership

The POML said "register any provisioning helper via `CommunicationModule`". The orchestration
constraint for this run assigns **task 010 sole ownership** of `Infrastructure/DI/CommunicationModule.cs`
and `Services/Communication/Acs/**` (parallel W1 task). To avoid a concurrent-edit conflict, task 012
registers its provisioning services in the **registration/provisioning module** (`RegistrationModule.cs`)
instead — still "the provisioning module", still ADR-032 unconditional. **Integration point for the main
session / task 010**: when 010's ACS identity/endpoint types land, a live `IAcsBoundaryProvisioner` (ARM
management SDK) MAY be registered in `CommunicationModule` and will supersede `DeferredAcsBoundaryProvisioner`
without changing `AcsBoundaryProvisioningService` or its callers (that is why the seam is an interface).

---

## 6. Scope boundary (what this task did NOT build)

Per the POML strict scope: this task provisions **resources + subscriptions only**. It does NOT implement
the webhook subscription-validation handshake, the Service Bus job, the normalizer, or idempotent persist
— those are **tasks 030/031**. The Event Grid subscription created here is what task 030's webhook consumes.

---

## 7. Deferred / follow-ups

- **Live provisioning** (§4) — DEFERRED to an operator with a real Azure subscription.
- **Live `IAcsBoundaryProvisioner`** — a future ARM-management-SDK implementation (or keep Bicep-only and
  leave the Null-Object as the in-process record). Registered in `CommunicationModule` by/after task 010.
- **ADR-046 body** — task 007 (main session) captures the residency rationale in §2 into the ADR.
