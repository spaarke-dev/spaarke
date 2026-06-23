# Phase 2 Live-Mode E2E Runbook

> **Author**: Task 087 (2026-06-22)
> **Status**: Deferred to post-operator-deploy (task 071)
> **Scope**: Activation procedure for the `[Trait("Category","Live")]` tests in `Phase2EndToEndTests.cs` once the Service Bus topic + Redis instance are provisioned.

---

## Why Deferred

Task 087's CI baseline uses **in-memory test doubles** (per the harness's Option 1 recommendation) to prove AC-1P2.3 through AC-1P2.8 against the production code paths *without* requiring Azure dependencies. This is necessary because:

1. The Service Bus topic `sprk-membership-changes` is **operator-deploy-gated** (task 071 ❌ `blocked-operator`).
2. The Redis pub/sub subscriber (task 086) requires a live `IConnectionMultiplexer` against an Azure Cache for Redis instance.
3. CI runners do not have outbound access to either resource.

The `[Trait("Category","Live")]` tests scaffold the activation surface so the post-deploy validation has a single landing target — operators don't have to author new tests from scratch when they cross task 071's runbook.

---

## Activation Steps (Post-Task-071 Topic Deploy)

### 1. Provision infrastructure

Per [`operator-followup-task071.md`](operator-followup-task071.md):

```bash
az deployment group create \
  --resource-group rg-spaarke-bff-prod \
  --template-file infra/bicep/membership-topic.bicep \
  --parameters topicName=sprk-membership-changes
```

Verify topic + subscription exist:

```bash
az servicebus topic show \
  --resource-group rg-spaarke-bff-prod \
  --namespace-name <namespace> \
  --name sprk-membership-changes

az servicebus topic subscription show \
  --resource-group rg-spaarke-bff-prod \
  --namespace-name <namespace> \
  --topic-name sprk-membership-changes \
  --name recon-junction-updater
```

### 2. Provision Redis (if not already)

If Phase 2 cache invalidation is being activated alongside the topic:

```bash
az redis show --resource-group rg-spaarke-bff-prod --name <cache-name>
```

Capture the connection string from the Azure portal (Access Keys blade).

### 3. Flip publisher + invalidator config flags

In the target environment's `appsettings.{Environment}.json` (or Key Vault):

```json
{
  "Membership": {
    "EventPublisher": {
      "Enabled": true
    },
    "JunctionUpdater": {
      "Enabled": true
    },
    "CacheInvalidator": {
      "Enabled": true
    }
  },
  "Redis": {
    "Enabled": true
  }
}
```

This swaps the three `Null*` peers (ADR-032 P2 Quiet no-op) for the real implementations.

### 4. Set env vars for the live-mode test run

```powershell
$env:SPAARKE_SB_NAMESPACE = "sb://<namespace>.servicebus.windows.net/"
$env:SPAARKE_REDIS_CONNECTION = "<host>:6380,password=...,ssl=true,abortConnect=false"
$env:SPAARKE_DATAVERSE_URL = "https://<env>.crm.dynamics.com"
# Plus whatever auth (managed identity in App Service, az login locally).
```

### 5. Run the live-mode tests

```powershell
dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ \
  --filter "Category=Live&FullyQualifiedName~Phase2EndToEndTests"
```

When env vars are absent the tests **skip via early return** (no failure, no false signal).

### 6. Implement the placeholder bodies

The two `LiveMode_*` tests currently `Assert.Fail` after the env-var gate. Their implementation is the activation deliverable:

#### `LiveMode_PublisherToServiceBusToHandler_E2E`

1. Boot a `WebApplicationFactory<Program>` instance wired to the LIVE `MembershipEventPublisher` + `MembershipJunctionUpdaterHost` (NOT the in-memory peers).
2. POST `/api/office/quickcreate/matter` with an authenticated test client.
3. Poll the LIVE Dataverse junction table (`sprk_userentityassociation`) via the Web API until the row appears or a 30-second timeout fires. Use `IDataverseService.RetrieveByAlternateKeyAsync` with the 5-tuple natural key.
4. Assert the row's fields match the published event payload.
5. Cleanup — delete the test matter + junction row to keep the live environment clean.

#### `LiveMode_RedisPubSubInvalidationE2E`

1. Bootstrap a live `ConnectionMultiplexer` to the configured Redis instance.
2. Subscribe a test handler to the `membership-cache-invalidate` channel BEFORE the POST.
3. POST `/api/office/quickcreate/matter`.
4. Assert the subscriber receives the invalidation payload within a 5-second timeout (Redis pub/sub is sub-second; include CI noise margin).
5. Assert the payload carries the correct `(personId, entityLogicalName, correlationId)` triple.

---

## Test Boundaries

The live-mode tests are **integration tests against production-shape infrastructure** — they require:
- A real Dataverse environment with the `sprk_userentityassociation` table provisioned (see task 070 + `scripts/Create-UserEntityAssociation.ps1`).
- A real Service Bus namespace + topic + subscription (task 071 runbook).
- A real Redis instance (existing Azure Cache for Redis in the BFF resource group).
- Test-isolation discipline — every test seeds + cleans its own data; runs MUST NOT interfere with concurrent activity in the environment.

For UAT / smoke-test purposes, run against a dedicated `spaarkedev1`-style environment, not production.

---

## Related Artifacts

- `tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/Phase2EndToEndTests.cs` — the test class with the `[Trait("Category","Live")]` scaffolds.
- `notes/operator-followup-task071.md` — Service Bus topic deploy runbook.
- `notes/event-source-inventory.md` — AC-1P2.3 inventory the publisher wiring points at.
- `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` — the kill-switch pattern the flag flips control.

---

*Update this runbook when the placeholder test bodies are filled in. Mark each `Implement the placeholder bodies` step ✅ as it ships.*
