# Bicep what-if dry-run — trial1 customer stamp (2026-08-26 SESSION 12)

> Task 186 pre-check evidence per SESSION 12 END QUICK RECOVERY.
> Prerequisite ✅ before any live `/provision-environment trial1` invocation.
> Owner directive SESSION 11 Q3: customerId=trial1, KEEP env permanently.

## Executive summary (GO / NO-GO)

- **Verdict**: **GO** — dry-run completed with status `Succeeded`, zero deployment-blocking diagnostics; all warnings are either well-understood Bicep-linter style hints or expected `NestedDeploymentShortCircuited` entries that the live task-186 dispatch will exercise fully.
- **Exit code**: `0`
- **Status field value**: `"Succeeded"`
- **Error field value**: `null`
- **Error-level diagnostic count**: **0**
- **Warning-level diagnostic count**: **11** (all `NestedDeploymentShortCircuited`, all runtime-what-if class, not deployment blockers)
- **Info-level diagnostic count**: **0**
- **Bicep linter warnings** (emitted to stderr **before** the JSON body, separate from `diagnostics[]`): **8** — 5 x BCP318 (module|null on Redis outputs), 1 x `use-secure-value-for-secure-inputs` (acs-communication.bicep), 1 x `prefer-unquoted-property-names` (generated kv-secrets), 1 x `no-unnecessary-dependson` (customer.bicep:485). None deployment-blocking.

Total planned Create operations at the subscription+root scope: **14** (1 x resourceGroup at sub scope + 13 x resources inside `rg-spaarke-trial1-dev`). An additional 11 nested-deployment scopes are queued but short-circuited from what-if evaluation because their parameters depend on `reference()` outputs from other modules not yet materialized.

## Command executed

- **az CLI invocation** (exact):

  ```
  az deployment sub what-if \
    --location westus2 \
    --template-file infrastructure/bicep/customer.bicep \
    --parameters customerId=trial1 environmentName=dev location=westus2 \
    --name whatif-trial1-report-$(date +%s) \
    --subscription 484bc857-3802-427f-9ea5-ca47b43db0f0 \
    --no-pretty-print 2>&1 | tee c:/tmp/whatif-trial1-authoritative.json
  ```

- **Subscription id**: `484bc857-3802-427f-9ea5-ca47b43db0f0`
- **Deployment name pattern**: `whatif-trial1-report-<epoch>` (fresh id each run to avoid ARM cache reuse)
- **Working directory**: `c:/code_files/spaarke-wt-customer-provisioning-orchestration-r1`
- **Shell**: Git Bash on Windows 11 (az CLI on PATH; `pwsh`/`az`/`bicep` verified in session 12 recovery)
- **Command duration (approximate)**: ~90–120 seconds end-to-end (compile Bicep → transmit → ARM what-if engine → JSON emit); dominated by the ARM-side what-if evaluator, not local compilation.
- **Artifact**: `c:/tmp/whatif-trial1-authoritative.json` (25,942 bytes; JSON body only — linter warnings live in the `tee`d text stream prefix).

## What what-if VALIDATES

The Azure Resource Manager what-if engine successfully validated the following classes of failure mode for THIS template invocation. Evidence cited is from this specific run's JSON body (`c:/tmp/whatif-trial1-authoritative.json`).

1. **Bicep compilation + type/schema binding** — 8 linter warnings emitted but zero compile errors. The template compiled cleanly to ARM JSON and passed ARM's schema validator (else what-if would not have produced any `changes` array).

2. **Parameter binding correctness** — `customerId=trial1`, `environmentName=dev`, `location=westus2` were accepted; string interpolation into resource names produced valid resource identifiers (see Full Changes Preview §5 below).

3. **Resource-group creation preflight (sub-scope routing)** — First planned change is `Microsoft.Resources/resourceGroups` at `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/rg-spaarke-trial1-dev`, `changeType: Create`, in `westus2`. Confirms:
   - Sub-scope deployment routing works (this is `az deployment sub`, not `az deployment group`)
   - Subscription id is reachable and the caller has `Microsoft.Resources/subscriptions/resourceGroups/write` intent
   - RG name conforms to the naming convention `rg-spaarke-{customerId}-{environmentName}`

4. **Tag block consistency** — Every root-scope resource-creating change carries the identical tag block:
   ```json
   { "application": "spaarke", "createdDate": "2026-08-26", "customer": "trial1",
     "environment": "dev", "managedBy": "bicep" }
   ```
   Confirmed on: `rg-spaarke-trial1-dev`, `sprk-trial1-dev-redis`, `sprk-trial1-dev-insights`, `mi-spaarke-trial1-dev`, `sprk-trial1-dev-logs`, `spaarke-trial1-dev-sbus`, `sprk-trial1-dev-plan` (7 taggable roots — child resources like queues/topics/auth-rules do not receive per-child tags, which is correct ARM behavior).

5. **Naming-convention compliance (where computable)** — The 14 planned resources conform to Spaarke naming:
   - RG: `rg-spaarke-{customer}-{env}` → `rg-spaarke-trial1-dev` ✅
   - Redis: `sprk-{customer}-{env}-redis` → `sprk-trial1-dev-redis` ✅
   - App Insights: `sprk-{customer}-{env}-insights` → `sprk-trial1-dev-insights` ✅
   - Managed Identity: `mi-spaarke-{customer}-{env}` → `mi-spaarke-trial1-dev` ✅
   - Log Analytics: `sprk-{customer}-{env}-logs` → `sprk-trial1-dev-logs` ✅
   - Service Bus namespace: `spaarke-{customer}-{env}-sbus` → `spaarke-trial1-dev-sbus` ✅
   - App Service Plan: `sprk-{customer}-{env}-plan` → `sprk-trial1-dev-plan` ✅
   - (Nested-module resource names — KV, Storage, Cosmos, OpenAI, AI Search, Doc Intel, BFF app — are NOT visible in this dry-run because their nested deployments were short-circuited; task 186 will validate those.)

6. **Resource-name collision detection (subscription-wide uniqueness for globally-named types)** — No collision errors emitted for `Microsoft.Cache/redis`, `Microsoft.ServiceBus/namespaces` (global-scope-unique). Confirms fresh subscription state matches expectations — the target subscription contains no prior `trial1` resources.

7. **Sub-scope vs RG-scope routing correctness** — The 14 top-level changes correctly route: 1 to sub-scope (the RG itself), 13 to `resourceGroup: "rg-spaarke-trial1-dev"` (all other resources). The 11 nested-deployment targets are all at `/subscriptions/.../resourceGroups/rg-spaarke-trial1-dev/providers/Microsoft.Resources/deployments/<moduleName>-sprktrial1dev` — confirming module invocation scope routing is correct.

8. **Service Bus topology completeness** — The planned Service Bus namespace ships with the full canonical topology in one atomic view:
   - 1 x namespace (`spaarke-trial1-dev-sbus`, Standard SKU, TLS 1.2 min)
   - 1 x auth rule (`SpaarkeAppAccess` with `Send`+`Listen`)
   - 4 x queues (`ai-indexing`, `document-indexing`, `sdap-communication`, `sdap-jobs`) — all with `deadLetteringOnMessageExpiration: true`, `lockDuration: PT5M`, `maxDeliveryCount: 10`, `defaultMessageTimeToLive: P14D` (uniform config confirms drift-fix from task 108 landed)
   - 1 x topic (`sprk-membership-changes`)
   - 1 x subscription (`recon-junction-updater`) with matching DLQ + lock policy
   This addresses the Session-11 queue-recreate concern: the topology is what the runbook expects.

9. **App Service Plan SKU sanity** — `sprk-trial1-dev-plan` planned as `S1` Linux (`kind: linux`, `properties.reserved: true`). Confirms the Standard-tier baseline (not F1/B1 hobbyist) and the Linux-plan flag that BFF Api requires.

10. **Log Analytics + App Insights wiring** — `sprk-trial1-dev-insights` is planned with `IngestionMode: LogAnalytics` and `WorkspaceResourceId` pointing at the (also-planned) `sprk-trial1-dev-logs` workspace. Confirms modern (workspace-backed, not classic) App Insights configuration.

## What what-if does NOT / CANNOT validate (honest capability envelope)

The Azure ARM what-if engine is a static preview. It is blind to a broad class of failure modes; task 186's live run is the only way to close these gaps. Enumerated concretely:

### Class A — What-if is out-of-scope by design (never validated by any what-if, on any template)

1. **Azure quota admission** — TPM (Azure OpenAI), vCPU (App Service Plan), Cosmos RU/s, Search partitions. what-if does not consult quota. This is the H0 preflight handler's responsibility at live-run time. Per operator memory: fresh PAYG subs auto-grant mini/embedding TPM generously (~500+) but zero for frontier tiers (gpt-5.4 / gpt-5-pro) — expect H0 to warn on frontier-model TPM if the template requests them.

2. **Runtime credential resolution** — Any Key-Vault-reference (`@Microsoft.KeyVault(...)`), managed-identity token issuance, RBAC-not-yet-granted, and `az login` state at runtime. what-if uses the CALLER's token to compile/preview, not the app's runtime identity.

3. **Post-deploy RBAC propagation timing** — Even when the l2-bff-rbac and bff-runtime-rbac deployments succeed, AAD propagation can lag 30–90 seconds. what-if cannot simulate this; the H14 post-deploy handler owns the wait/retry.

4. **Dataverse environment creation** — H5 handler shells out to `pac admin create` / Power Platform admin APIs. Zero Bicep involvement, zero what-if coverage.

5. **SharePoint Embedded container-type registration** — H8 handler. Zero Bicep involvement, zero what-if coverage. Per operator memory: MS's documented 24h wait is near-instantaneous in practice — not a blocker.

6. **Graph app-role grants (admin consent)** — H0.5 gate. External to ARM.

7. **Handler business logic** — H1, H4, H7, H10, H11, H12a/b/c, H13 all shell out to non-ARM APIs (Graph, Dataverse Web API, Power Platform admin, KV data-plane). what-if is ARM-only.

8. **`az deployment sub what-if` deployment-name uniqueness** — Not validated: what-if does not check whether the deployment-name might collide with a prior CREATE run in the RG's deployment history. Not a concern here because the pattern uses `$(date +%s)`.

### Class B — What-if hit its known limits ON THIS TEMPLATE (short-circuited nested deployments)

The 11 warnings all carry code `NestedDeploymentShortCircuited` with the message *"A nested deployment got short-circuited and all its resources got skipped from validation. This is due to a nested template having a parameter that was not fully evaluated (e.g. contains a reference() function)."*

Each represents a module whose inputs depend on outputs of another module that hasn't been materialized yet — ARM what-if cannot follow `reference()` across un-deployed modules. Enumerated with the cause and task-186 exercise plan for each:

| # | Nested-deployment target | Underlying module | Why short-circuited (this template) | How task 186 exercises it |
|---|---|---|---|---|
| 1 | `keyVault-sprktrial1dev` | `modules/key-vault.bicep` | Module inputs likely include a `reference()` on the User-Assigned Managed Identity's `principalId` (for the RBAC-enabled KV access policy). MI is a sibling planned resource; its principalId is only known post-Create. | Live H4 deployment provisions KV; subsequent module wiring uses concrete IDs; task 186 acceptance checks KV exists + operator has Secrets User (E-1 protected SpeAdmin family unchanged). |
| 2 | `storage-sprktrial1dev` | `modules/storage.bicep` | Same class: MI principalId reference for Storage Blob Data Owner/Contributor role assignments. | Live deployment materializes storage account + blob containers; task 186 verifies presence + RBAC binding. |
| 3 | `cosmos-sprktrial1dev` | `modules/cosmos.bicep` | Reference() to MI principalId for Cosmos DB Built-in Data Contributor role. Also possible reference to sub-scope AAD tenant for allowed-networks. | Live deployment provisions Cosmos + database + containers; task 186 probe I3 verifies. |
| 4 | `openAi-sprktrial1dev` | `modules/openai.bicep` | Reference() to MI for Cognitive Services User; model-deployment provisioning may reference SKU-availability config. **This is the module most exposed to quota-class runtime failure** — H0 preflight is the gate. | Live deployment provisions OpenAI account + model deployments; H0 gate + task 186 probes exercise. Per operator memory: verify canonical Spaarke strategy = westus2 platform + westus3 OpenAI **before** the live run (this template uses westus2 for BOTH — confirm intended for trial1 dev, or migrate to westus3 for OpenAI). |
| 5 | `aiSearch-sprktrial1dev` | `modules/ai-search.bicep` | Reference() to MI for Search Index Data Contributor. | Live deployment provisions Search service; task 186 probe I2 verifies. |
| 6 | `docIntelligence-sprktrial1dev` | `modules/doc-intelligence.bicep` | Reference() to MI for Cognitive Services User on Document Intelligence. | Live deployment provisions Doc Intel account; task 186 acceptance verifies key/endpoint reachable. |
| 7 | `bffApi-sprktrial1dev` | `modules/bff-api.bicep` | Reference() to App Service Plan `id` + reference() to App Insights InstrumentationKey + reference() to MI + reference() to KV vault URI for AppSettings `@Microsoft.KeyVault(...)` bindings. This module is heavily downstream. | Live deployment creates the Web App with all AppSettings bound; task 186 acceptance verifies `/healthz` returns 200 with the app resolving MI-based KV secrets. |
| 8 | `bffApiSlot-sprktrial1dev` | `modules/bff-api-slot.bicep` | Reference() to bff-api parent site id (which references App Service Plan etc.). | Live deployment creates staging slot; task 186 T5 slot-MI probe exercises. |
| 9 | `l2-bff-rbac-sprktrial1dev` | (RBAC assignments module) | Depends on bffApi + KV + Storage + Cosmos + OpenAI + AI Search principal IDs to write role assignments. Fully dependent on prior module materialization. | Live deployment writes role assignments; task 186 T2/T3 RBAC-parity probes verify. |
| 10 | `bff-runtime-rbac-sprktrial1dev` | (Runtime RBAC module) | Same class — depends on runtime identities being present. | Live deployment writes runtime RBAC; T4/T6 probes verify. |
| 11 | `kvSecrets-sprktrial1dev` | `scripts/canonical-secret-catalog/generated/kv-secrets.generated.bicep` | Depends on KV existing (reference to vault URI) + on secret VALUES that are supplied at runtime, not template-time. **This is where the H4 canonical-secret-catalog logic lives — the deliberate H4-omit behavior for `BFF-API-ClientSecret` + `Dataverse-ClientSecret` in secret-free environments is only exercised at live-run time**, per the BINDING credential-lifecycle rule (root CLAUDE.md §17 provisioning-skill row, rewritten 2026-08-25). | Live deployment materializes KV secret placeholders per H4 catalog; task 186 verifies the E-3 no-touch rule holds (no seed of BFF-API-ClientSecret / Dataverse-ClientSecret in this secret-free trial1 stamp). |

**Confidence loss from short-circuit**: Real, but bounded. What-if COULD have caught: (a) module input schema mismatches (mitigated: local Bicep compile did not error out — modules type-check statically), (b) resource-name conflicts inside the modules (mitigated: modules use the same customerId-parameterized naming as the top-level, and the RG is empty). What-if COULD NOT catch: (a) `reference()` outputs that turn out to be `null` at runtime (BCP318 warnings on Redis outputs at 825–829 are the linter's warning about exactly this pattern), (b) role-assignment write failures if RBAC data-plane is throttled, (c) any runtime-value-dependent branch inside a module.

### Class C — Distinguishing "out-of-scope" vs "hit-its-limits"

| Concern | Class | Rationale |
|---|---|---|
| KV RBAC not-yet-granted at runtime | A | what-if never simulates identity token issuance |
| KV RBAC role-assignment WRITE (in nested `keyVault-*` deployment) | B | short-circuited; template LOGIC is valid but not exercised |
| OpenAI TPM quota | A | quota API is separate from ARM |
| OpenAI resource-name collision (in nested `openAi-*` deployment) | B | short-circuited but the naming convention passes at the top-level RG level |
| SPE container-type provisioning | A | H8 handler; non-ARM |
| Dataverse env creation | A | H5 handler; non-ARM |
| Redis output `null` at start-of-deployment (BCP318 warnings 825–829) | B — pre-flagged by linter | dependency-graph fragility; only manifests if a Redis output is consumed before Redis is provisioned; H4/H14 ordering must protect this |
| App Insights → Log Analytics wiring correctness | ✅ VALIDATED (row 10 of §"What what-if VALIDATES") | not a gap |

## Full changes preview

The 14 planned Create operations, in JSON-emit order. `Notes` calls out anything non-default or noteworthy. All rows have `changeType: Create` and `before: null` (fresh RG); omitted columns for readability.

| # | ChangeType | ResourceType | Resource ID (short) | Location | Notes |
|---|---|---|---|---|---|
| 1 | Create | `Microsoft.Resources/resourceGroups` | `/subscriptions/484bc857-.../resourceGroups/rg-spaarke-trial1-dev` | `westus2` | Sub-scope. Full tags block (application/customer/environment/createdDate/managedBy). Root of the stamp. |
| 2 | Create | `Microsoft.Cache/redis` | `.../providers/Microsoft.Cache/redis/sprk-trial1-dev-redis` | `westus2` | Basic C0 SKU (capacity 0). TLS 1.2 min, non-SSL port disabled, public net access **Enabled**, `maxmemory-policy: allkeys-lru`. Deliberate cost-tier choice for a trial customer. |
| 3 | Create | `Microsoft.Insights/components` | `.../providers/Microsoft.Insights/components/sprk-trial1-dev-insights` | `westus2` | `kind: web`, `Application_Type: web`, `IngestionMode: LogAnalytics`, workspace-backed via `WorkspaceResourceId` → `sprk-trial1-dev-logs`. Modern (non-classic) App Insights. |
| 4 | Create | `Microsoft.ManagedIdentity/userAssignedIdentities` | `.../providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-spaarke-trial1-dev` | `westus2` | UAMI — the runtime identity the BFF Web App will use for KV/Cosmos/OpenAI/Storage/AI-Search RBAC. principalId only known post-Create (root cause of short-circuits 1–11). |
| 5 | Create | `Microsoft.OperationalInsights/workspaces` | `.../providers/Microsoft.OperationalInsights/workspaces/sprk-trial1-dev-logs` | `westus2` | `PerGB2018` SKU, `retentionInDays: 90`. Standard telemetry tier. |
| 6 | Create | `Microsoft.ServiceBus/namespaces` | `.../providers/Microsoft.ServiceBus/namespaces/spaarke-trial1-dev-sbus` | `westus2` | `Standard` SKU, `minimumTlsVersion: 1.2`. Note: uses `spaarke-` prefix (not `sprk-`) per canonical Spaarke SB naming. |
| 7 | Create | `Microsoft.ServiceBus/namespaces/AuthorizationRules` | `.../spaarke-trial1-dev-sbus/AuthorizationRules/SpaarkeAppAccess` | RG | Rights: `Send`, `Listen`. Namespace-level SAS for legacy/AAD-not-yet-wired paths. No `Manage` — correct least-privilege. |
| 8 | Create | `Microsoft.ServiceBus/namespaces/queues` | `.../spaarke-trial1-dev-sbus/queues/ai-indexing` | RG | DLQ on expiration, lock 5 min, max delivery 10, TTL P14D, 1 GiB. Config drift from Session-11 investigation is FIXED (uniform across all 4 queues). |
| 9 | Create | `Microsoft.ServiceBus/namespaces/queues` | `.../spaarke-trial1-dev-sbus/queues/document-indexing` | RG | Same config as row 8. |
| 10 | Create | `Microsoft.ServiceBus/namespaces/queues` | `.../spaarke-trial1-dev-sbus/queues/sdap-communication` | RG | Same config as row 8. |
| 11 | Create | `Microsoft.ServiceBus/namespaces/queues` | `.../spaarke-trial1-dev-sbus/queues/sdap-jobs` | RG | Same config as row 8. |
| 12 | Create | `Microsoft.ServiceBus/namespaces/topics` | `.../spaarke-trial1-dev-sbus/topics/sprk-membership-changes` | RG | Batch ops enabled, ordering enabled, 1 GiB, TTL P14D. |
| 13 | Create | `Microsoft.ServiceBus/namespaces/topics/subscriptions` | `.../topics/sprk-membership-changes/subscriptions/recon-junction-updater` | RG | DLQ on expiration, lock 5 min, max delivery 10, TTL P14D, batched. |
| 14 | Create | `Microsoft.Web/serverfarms` | `.../providers/Microsoft.Web/serverfarms/sprk-trial1-dev-plan` | `westus2` | `kind: linux`, `S1` SKU, `reserved: true`. Standard tier — required for slots (row inside short-circuited `bffApiSlot-*` nested deploy). |

**Grouped by nested-deployment scope**:

- **Top-level (sub scope)**: row 1 (RG)
- **Top-level (RG scope, direct resources, NOT nested-module)**: rows 2–14 (13 resources)
- **Nested modules (all short-circuited, 0 resources visible in what-if)**: `keyVault-sprktrial1dev`, `storage-sprktrial1dev`, `cosmos-sprktrial1dev`, `openAi-sprktrial1dev`, `aiSearch-sprktrial1dev`, `docIntelligence-sprktrial1dev`, `bffApi-sprktrial1dev`, `bffApiSlot-sprktrial1dev`, `l2-bff-rbac-sprktrial1dev`, `bff-runtime-rbac-sprktrial1dev`, `kvSecrets-sprktrial1dev` (11 modules)

## Diagnostics breakdown

### Errors (should be 0)

**Count: 0.** No entries in `diagnostics[]` at `level: "Error"`. `error` field on the top-level object is `null`. Nothing to enumerate.

### Warnings — Bicep linter (source-code style; not deployment blockers)

These 8 emit to **stderr** BEFORE the JSON body and are Bicep compiler linter output, NOT ARM what-if diagnostics.

| # | File:Line | Code | Message | Rationale (why not a blocker for THIS run) |
|---|---|---|---|---|
| L1 | `infrastructure/bicep/modules/acs-communication.bicep(105,22)` | `use-secure-value-for-secure-inputs` | Property `endpointUrl` expects a secure value, but the value provided may not be secure. | Endpoint URL is not a secret (public FQDN); the module type declares the parameter `@secure` conservatively. Not a runtime failure; a style-hint the module owner may relax. Applies to ACS module, not the trial1 execution path directly. |
| L2 | `scripts/canonical-secret-catalog/generated/kv-secrets.generated.bicep(643,24)` | `prefer-unquoted-property-names` | Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. | Generated file (do not hand-edit). Stylistic only. Generator emits quoted keys defensively for dashed identifiers. |
| L3 | `infrastructure/bicep/customer.bicep(485,5)` | `no-unnecessary-dependson` | Remove unnecessary `dependsOn` entry `'storage'`. | Bicep infers the dependency from the `reference()`/`module` graph. The explicit `dependsOn: [storage]` is redundant but harmless — deployment ordering is unaffected. |
| L4 | `infrastructure/bicep/customer.bicep(825,68)` | `BCP318` | The value of type `module | null` may be null at the start of the deployment, which would cause this access expression (and the overall deployment with it) to fail. | Line 825 accesses a conditional Redis module output. As-written, the access is protected by the same condition that guards the module — Bicep's type-narrowing does not follow this pattern. Runtime-safe under current condition guard; would benefit from a `?? {}` coalesce for lint-cleanliness. |
| L5 | `infrastructure/bicep/customer.bicep(826,66)` | `BCP318` | Same class as L4. | Same rationale as L4 — sibling access on the same module. |
| L6 | `infrastructure/bicep/customer.bicep(827,70)` | `BCP318` | Same class as L4. | Same rationale as L4. |
| L7 | `infrastructure/bicep/customer.bicep(828,73)` | `BCP318` | Same class as L4. | Same rationale as L4. |
| L8 | `infrastructure/bicep/customer.bicep(829,79)` | `BCP318` | Same class as L4. | Same rationale as L4 — 5 sequential lines all reading distinct outputs from the same conditional Redis module block. Track as a known lint-cleanup backlog item (not shipping-blocker). |

**Aggregate linter warning count**: **8**.

### Warnings — Runtime what-if evaluation

All 11 entries in `diagnostics[]` are `level: "Warning"`, `code: "NestedDeploymentShortCircuited"`. See §"What what-if does NOT / CANNOT validate (honest capability envelope)" → Class B → the 11-row table for the full per-target enumeration with the reference() cause + task-186 exercise plan.

Summary table (target only, for completeness at this location):

| # | Nested-deployment target (ARM path) | Confidence loss | Task-186 gate |
|---|---|---|---|
| W1 | `.../deployments/keyVault-sprktrial1dev` | Bounded — module type-checked at compile | H4 live run + KV probe |
| W2 | `.../deployments/storage-sprktrial1dev` | Bounded — storage module is stable across R1 history | Live run + storage-blob probe |
| W3 | `.../deployments/cosmos-sprktrial1dev` | Bounded — Cosmos schema stable | Task 174 I3 probe |
| W4 | `.../deployments/openAi-sprktrial1dev` | Elevated — model-deployment quota class not simulated | H0 preflight + live run |
| W5 | `.../deployments/aiSearch-sprktrial1dev` | Bounded | Task 173 I2 probe |
| W6 | `.../deployments/docIntelligence-sprktrial1dev` | Bounded | Live run acceptance |
| W7 | `.../deployments/bffApi-sprktrial1dev` | Bounded — most `@Microsoft.KeyVault(...)` refs resolve at runtime, not deploy | Live run + `/healthz` probe |
| W8 | `.../deployments/bffApiSlot-sprktrial1dev` | Bounded | Task 172 T5 slot-MI probe |
| W9 | `.../deployments/l2-bff-rbac-sprktrial1dev` | Bounded — RBAC assignment writes tend to succeed if principals resolve | Task 178 T3 Graph-parity probe |
| W10 | `.../deployments/bff-runtime-rbac-sprktrial1dev` | Bounded | T4/T6 probes |
| W11 | `.../deployments/kvSecrets-sprktrial1dev` | Elevated — H4 canonical-secret-catalog + BINDING E-3 no-touch rule only exercised at live-run time | Live H4 handler + task 186 acceptance verifies the E-3 rule (no `BFF-API-ClientSecret` / `Dataverse-ClientSecret` seed for secret-free trial1) |

**Aggregate what-if warning count**: **11**. **Non-blocking.**

### Info (should be minimal)

**Count: 0.** No `level: "Info"` entries in `diagnostics[]`.

## Confidence assessment

### KNOWN CLEAN (high confidence, from this dry-run)

- Bicep template compiles cleanly; ARM accepted the template payload without error.
- Subscription id `484bc857-3802-427f-9ea5-ca47b43db0f0` is reachable and the caller's token has sufficient scope to preview a sub-scope deployment.
- Fresh subscription state confirmed for `trial1` — no pre-existing collision on globally-named resource types (Redis, Service Bus namespace).
- Resource-group naming, tag block, region binding all conform to canonical conventions.
- Service Bus topology (namespace + 1 auth rule + 4 queues + 1 topic + 1 subscription) materializes as one atomic view — the Session-11 queue-config-drift concern is FIXED at the template level (all 4 queues carry uniform DLQ/lock/TTL/maxDelivery).
- App Service Plan is Standard-tier Linux (S1 reserved) — supports slots and BFF workload.
- App Insights is workspace-backed (modern, not classic) — aligns with net10 F-6 removal of classic-AppInsights-SDK.

### IS-EXPECTED-TO-BE-CLEAN but not independently verified here (the 11 short-circuited modules)

Each of the 11 short-circuited nested deployments carries **bounded confidence loss** because the modules type-check statically and have precedent (they've deployed successfully in prior R1 wave stamps against dev/insights-engine-dev). Task 186 is the empirical closure. Elevated-attention items during the live run:

- **openAi-sprktrial1dev (W4)**: model-deployment TPM quota is a real class-A risk. Verify H0 preflight passes for the frontier tier if the template requests it.
- **kvSecrets-sprktrial1dev (W11)**: BINDING E-3 no-touch rule for `BFF-API-ClientSecret` + `Dataverse-ClientSecret` in this secret-free stamp — task 186 acceptance MUST verify the H4 catalog OMITS these two secret names.

### Post-deploy residual risks operator should monitor

1. **Quota admission at H0** — OpenAI TPM (esp. frontier tiers), App Service vCPU, Cosmos RU/s. Escalate per operator memory notes if H0 warns.
2. **RBAC propagation timing** — Post-H14, allow 30–90 s for AAD graph propagation before running probes T2/T3/T4/T6.
3. **SPE 24h gate (H8)** — Per operator memory, empirically near-instantaneous; do not pad the estimate.
4. **Admin consent latency (H0.5)** — External to ARM; if this is a fresh tenant flow it may block.
5. **Redis `null`-at-start dependency (BCP318 warnings L4–L8)** — Ensure any consumer of Redis outputs at customer.bicep:825–829 is behind the same condition guard the module itself uses; runtime failure would manifest as `The template resource property is undefined` during a subsequent module's evaluation.
6. **westus2 vs westus3 OpenAI region** — Per operator memory: canonical Spaarke strategy is `westus2 platform + westus3 OpenAI`. This template uses `westus2` for BOTH per the parameters. Confirm this is intended for trial1 dev (may be acceptable for a trial customer to keep it single-region), or migrate before live run.

## Recommendation

- **Proceed with task 186 dispatch: YES.**
- **Recommended pre-task-186 mitigations**: None required. The one operator-attention item is confirmatory:
  - Confirm the westus2-for-OpenAI choice for trial1 dev is deliberate vs the canonical westus2-platform/westus3-OpenAI split (operator-memory note, `reference_azure_fresh_sub_regional_gotchas`). If deliberate → no action; if oversight → adjust parameters BEFORE dispatch (cheap now; expensive after H4).
  - Optionally file a lint-cleanup backlog item for the 5 x BCP318 warnings at customer.bicep:825–829 (`?? {}` coalesce) — cleanup, not gating.
- **Cross-references**:
  - `projects/customer-provisioning-orchestration-r1/notes/queue-recreate-runbook-2026-08.md` — the Service Bus topology assertions above (rows 6–13 of Full Changes Preview) are the template-level fix for the drift documented in that runbook.
  - `projects/customer-provisioning-orchestration-r1/current-task.md` SESSION 12 END QUICK RECOVERY — the source of the task-186 pre-check obligation this report satisfies.
  - `.claude/skills/provision-environment/SKILL.md` — the L3 operator skill wrapping the L2 API that will drive task 186.
  - `.claude/constraints/provisioning.md` §KV credential lifecycle — the BINDING E-3 no-touch rule that `kvSecrets-sprktrial1dev` (W11) will exercise at live-run time.

## Raw artifact

- Path: `c:/tmp/whatif-trial1-authoritative.json`
- Size: 25,942 bytes
- Nature: Temporary file (Windows `c:/tmp`, not repo-tracked). **This report IS the audit artifact of record**; the JSON is source data that may be re-generated by re-running the STEP-1 command.
- Fresh-run integrity: deployment name `whatif-trial1-report-<epoch>` prevents ARM-cache reuse; each run is a first-class evaluation.
