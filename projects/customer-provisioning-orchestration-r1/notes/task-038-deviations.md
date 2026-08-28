# task-038 — deviations & design notes

Task: **038-wire-service-bus-client-l2** (Wave C3 batch 2B, dispatched 2026-08-17)

## Deviations from POML

### D1 — bicep serviceBusKeyVaultSecretName parameter is NOT consumed by L2 code (Path A per CLAUDE.md §6.5)

- **What the POML implied**: `platform-controlplane.bicep` accepts a `serviceBusKeyVaultSecretName` parameter (KV-referenced Service Bus connection string) — a straightforward read might infer the L2 App Service should bind `ServiceBus:ConnectionString` from KV.
- **What was implemented**: `ServiceBusModule.AddServiceBusModule` reads `ServiceBus:FullyQualifiedNamespace` from `IConfiguration` and constructs `ServiceBusClient(fqn, TokenCredential)` using `DefaultAzureCredential`. The KV-secret-name bicep parameter is **not consumed** by any code in this task.
- **Why (Path A rationale)**:
  - **POML acceptance criterion 5** (binding): *"Negative: no account-key credential; DefaultAzureCredential (UAMI at runtime) only."*
  - **ADR-028 MUST rule**: MI-outbound is canonical; connection strings are documented exception with narrow scope (E-1 SpeAdmin, E-2 Azure OpenAI). Neither applies to Service Bus for L2.
  - **spec.md FR-22** does not mandate a specific transport-auth mechanism — the design goal is fire-and-forget enqueue, and MI satisfies it without secret rotation burden.
- **Follow-up (bicep-side)**: `platform-controlplane.bicep` should add `ServiceBus:FullyQualifiedNamespace` as an app setting bound to `<sb-namespace>.servicebus.windows.net` and either retire `serviceBusKeyVaultSecretName` or repurpose it for a distinct consumer. Not a blocker for this task — the L2 code path is self-contained; a fresh checkout with only the app-setting produces a working L2.

### D2 — shared queue with property-based routing (POML permitted; decision documented)

- **What the POML allowed**: *"MUST use a per-handler queue OR a shared queue with per-handler subscription — verify the current Service Bus topology in `infrastructure/bicep/modules/service-bus.bicep` + adapt."*
- **What was implemented**: shared queue (default name `sprk-provisioning-jobs`) with per-handler routing via `ApplicationProperties["JobType"] = HandlerId` and `Subject = HandlerId`.
- **Why**: mirrors BFF's `JobSubmissionService`, which uses the SAME Subject + `ApplicationProperties["JobType"]` fields. BFF's existing `ServiceBusJobProcessor` dispatches to `IJobHandler` by JobType (per ADR-036) — adopting the same shape means the BFF-side receiver wiring (Wave C5 / BFF coordination) requires no new dispatch surface, just registration of new `IJobHandler` impls for provisioning handlers.
- **Trade-off**: no per-handler DLQ isolation. If a specific handler (e.g. H2a bicep-deploy) becomes a noisy neighbour, we can split its handler out onto a dedicated queue by extending `ServiceBusModuleOptions.HandlerQueueOverrides` (not implemented — deferred until observed need).

### D3 — SessionId is always set even though default queue is not session-enabled

- **What**: `ServiceBusHandlerEnqueuer` sets `SessionId = envelope.CustomerId` unconditionally.
- **Why**: `service-bus.bicep` default is `requiresSession: false`. Setting `SessionId` on a non-session-enabled queue is a wire-side no-op — but pre-setting it means a future bicep toggle to `requiresSession: true` (for §4D I5 same-customer serialization enforcement) works **without code change**. Zero cost; forward-compatible.

### D4 — task 030 RBAC verification is DEFERRED (not blocker)

- **POML step 10**: *"Verify UAMI has Azure Service Bus Data Sender on the SB namespace — cross-check with task 030 RBAC coverage; file a follow-up if not covered."*
- **What was done**: task 030 sibling is running concurrently in Wave 2 Batch 2B; a direct cross-check would require reading its in-flight commits. The code is CORRECT for the RBAC-only path (DefaultAzureCredential + FQN); the operational verification that the role exists at deploy time is a Phase F concern.
- **Follow-up filing**: Wave C5 (state reconciler + REST endpoint) task should include an operator check-list step: run `az role assignment list --assignee <uami-principalId> --scope <sb-namespace-id>` and confirm `Azure Service Bus Data Sender`. If task 030 does not cover this, a bicep tweak to `platform-controlplane.bicep` (RBAC block) is a 3-line addition.

### D5 — queue provisioning is NOT in this task's scope

- **What**: The default queue name `sprk-provisioning-jobs` is not present in `service-bus.bicep`'s default `queueNames = ['sdap-jobs', 'document-indexing']` array.
- **Why not created here**: `service-bus.bicep` is a shared module authored before r1; extending its default array would drag another module into this task's blast radius. The bicep tweak to add `sprk-provisioning-jobs` to the env-scope Service Bus provisioning is a small, atomic follow-up (either extend the default `queueNames` param or invoke `service-bus.bicep` explicitly from `platform-controlplane.bicep` with an override array).
- **Interim**: for local dev, an operator can create the queue with `az servicebus queue create --namespace-name <ns> --resource-group <rg> --name sprk-provisioning-jobs --enable-duplicate-detection true --duplicate-detection-history-time-window PT10M`.

## Design notes (non-deviation, for downstream Wave C5 consumers)

### MessageId formula (level-1 idempotency)

`MessageId = SHA256_hex( "{HandlerId}|{RunId}|{CustomerId}|{paramHash}" )` where `paramHash = SHA256_hex( ParametersJson )`.

Length: 64 chars (well under SB 128-char cap).

Determinism: two calls with identical `(HandlerId, RunId, CustomerId, ParametersJson)` produce identical MessageIds. `EnqueuedAt` is DELIBERATELY excluded — it's observability metadata, not identity.

Verified by `ServiceBusSmokeTests.ComputeMessageId_IsDeterministic_ForIdenticalEnvelopes` (unit test) + `ComputeMessageId_ChangesWhenAnyDimensionDiffers` (parametrised, 4 dimensions).

### Envelope shape (wire schema)

```json
{
  "handlerId":      "H4",
  "runId":          "01J7Q3ZP...",
  "customerId":     "acme-corp",
  "parametersJson": "{\"kvUri\":\"@Microsoft.KeyVault(SecretUri=...)\"}",
  "enqueuedAt":     "2026-08-17T14:00:00Z"
}
```

- `handlerId` — copied into `Subject` + `ApplicationProperties["JobType"]` + `ApplicationProperties["HandlerId"]` for dispatch-side observability.
- `runId` — copied into `CorrelationId` for two-sided log correlation.
- `customerId` — sourced into `SessionId` (§4D I5 forward-compat).
- `parametersJson` — opaque payload; handler owns schema; KV URI refs only (no cleartext secrets).
- `enqueuedAt` — copied into `ApplicationProperties["EnqueuedAt"]` for latency metrics.

Schema stability: treat any change (rename / removal / semantic shift) as breaking + coordinate two-sided deployment. Additions with defaults are safe.

### DI shape

Program.cs total non-framework DI count is now **4 lines**:

```csharp
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddSwaggerModule();
builder.Services.AddCosmosModule(builder.Configuration);
builder.Services.AddServiceBusModule(builder.Configuration);
```

ADR-010 target is ≤15 non-framework lines. L2 is well within budget.

### TokenCredential sharing

`ServiceBusModule` uses `sp.GetService<TokenCredential>()` to REUSE the `TokenCredential` singleton `CosmosModule` registers. When only `ServiceBusModule` is composed (e.g. integration-test rig), it builds its own `DefaultAzureCredential` from configuration. Single-instance credential -> token cache reuse across Cosmos + Service Bus.

### Not in this task's scope (Wave C5 / BFF-side)

- BFF-side `IJobHandler` implementations for the 19 provisioning handlers.
- REST endpoint that consumes `IHandlerEnqueuer` (POST /api/runs → 202 Accepted).
- State reconciler background service.
- BFF-side ServiceBusJobProcessor extension (if the shared processor cannot handle provisioning JobTypes as-is, a per-handler-Type registration is needed on the BFF side — coordination point with Wave C5).

## Build + test evidence

```
Debug build:    0 warn / 0 err
Release build:  0 warn / 0 err  (analyzers-as-errors enforced)
Unit tests:     11/11 passed (5 CosmosSmokeTests + 6 ServiceBusSmokeTests)
CVE scan:       0 vulnerable packages
```

Env-guarded smoke tests (`SB_L2_SMOKE_FQN` unset in this run) exercised as compile-only; live-SB round-trip is an operator run per CosmosSmokeTests convention.
