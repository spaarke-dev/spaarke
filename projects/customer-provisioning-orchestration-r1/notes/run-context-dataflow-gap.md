# Run-context data flow: handler inputs with no producer (plan G25)

> **Found**: 2026-09-30 SESSION 28, while scoping T238 (`Customer__Id` via H4b).
> **Method**: read-only audit of every `run.Parameters.NonSecret` read and every `InterStepState` field across
> `Sprk.Provisioning.ControlPlane.{Api,Core,Worker}` (tests excluded), spot-checked by hand
> (`KeyVaultCertBootstrapProbe.cs:92`, `H2aBicepInfraDeployHandler.cs:270`, `H4bBulkAppSettingsHandler.cs:171-222,281`,
> `DagAdvancer.cs:140-163`).

## 1. Finding

A real provisioning run **cannot get past H0**. Handlers read about twenty required inputs from
`run.Parameters.NonSecret` that nothing writes:

- The **only** production write to `Parameters.NonSecret` is the intake copy at `RunsEndpoints.cs:645`, which
  copies the CreateRun request's `nonSecretParameters` bag verbatim. `/provision-environment` Step 4.0 sends 11
  keys: `tenantId, subscriptionId, openAiLocation, confirmationAcknowledgment, intakeFileSha256, region, tier,
  estimatedMonthlyUsd, costEnvelopePolicy, operatorUpn, containerTypeId`.
- No handler, reconciler, dispatcher or envelope builder copies handler outputs or ARM deployment outputs into
  `NonSecret`. `HandlerEnvelope` has no `Parameters` property (`HandlerEnvelope.cs:92-145`), so the many comments that
  say "`envelope.Parameters.NonSecret`" really mean `run.Parameters.NonSecret`. Comments that say "H1 / H0.5 / H2a
  MUST populate X" (`H2a:263`, `H4:323`, `H4:351`, `H3:289`) describe a promotion step that was never built — the
  same step `IPerEnvSettingsManifest.cs:109` and `manifest.yaml:807` assume for H4b.
- Unit tests seed the missing keys by hand (e.g. `H4bBulkAppSettingsHandlerTests.cs:651-658`), so every handler
  passes in isolation.

### Where a real run stops (DAG order)

| Handler | First missing input | Rejection code (all Resumable) |
|---|---|---|
| **H0** | `keyVaultName` (SPE-cert bootstrap probe, `KeyVaultCertBootstrapProbe.cs:92`) | `spe-cert-bootstrap-missing` |
| H2a | `bicepVer` (`H2a:270`) | `missing-bicep-version` |
| H4 | `keyVaultName` (`H4:326`), then `resourceGroupName`, `appServiceName`, `userAssignedIdentityResourceId`, `secretsVer` | `kvsecrets-missing-kv-name` … |
| H4b | `keyVaultName` (`H4b:188`), then `resourceGroupName`, `appServiceName`, `environmentName`, `secretsVer`; and all 7 `per_env_settings` sources (`kv_vault_uri, cosmos_endpoint, tenant_id, bff_app_client_id, container_type_id, uami_client_id, service_bus_fqns`) — snake_case keys nothing writes (the skill's `tenantId`/`containerTypeId` are camelCase) | `h4b-missing-kv-name`, `h4b-per-env-input-missing` |
| H2b | `indexVer` (`H2b:246`) | `missing-index-version` |
| H9 | `resourceGroupName`, `appServiceName` (`H9:329,336`) | `missing-resource-group-name` … |
| H11 | `identityPreset`, `usersJson` (`H11:235,255`) | `userprov-missing-*` |
| H12b | `dataverseUrl` from NonSecret (`H12b:255`) although H5 writes `InterStepState.DataverseEnvUrl` | `appconfig-missing-dataverse-url` |
| H13 | `buildId`, `bffApiUrl` (`H13:255,261`); RG / App Service / vault read as optional → T1/T5 trap probes InfraFault | `h13-missing-build-id` … |
| H14 | `exchangePolicyScopeGroupId`, `webhookNotificationBaseUrl`, at least one webhook target (`H14:243-312`) | `h14a-*`, `h14-*` |

### Values that exist but are dropped

H2a's ARM runner already captures `ResourceGroupName`, `AppServiceName`, `AppServiceStagingSlotName`,
`UserAssignedIdentityResourceId` (`BicepDeployOutputs.cs:29-60`, mapped at `ArmDeploymentRunner.cs:465-479`), but
H2a persists only five fields to `InterStepState` (`H2a:797-801`: OpenAi/AiSearch/Cosmos endpoints, MI object +
client id). `customer.bicep` also outputs `keyVaultName`, `keyVaultUri` (`:796-797`) and the Service Bus
name/endpoint (`:807-808`); nothing maps them. There is no C# naming helper for the Key Vault or App Service name —
those names exist only in Bicep (`customer.bicep:174,571`).

### Related defects found by the same audit

- **`keyVaultName` names two different vaults**: the Spaarke platform vault holding the SPE owning-app certificate
  (H0 probe, H8 — `KeyVaultCertBootstrapProbe.cs:30-33`) and the customer vault (H3, H4, H4b, H14).
- **`region` / `tier` carry two meanings**: Azure region + cost tier for H0; Dataverse region + `environmentSku` for H5
  (`BapRestEnvironmentCreator.cs:333`).
- **`environmentName`**: H2a/H2b silently default to `"prod"` (`H2a:151`, `H2b:145`); H4b requires it with no
  default; nothing sends it. The skill's `$env` (dev|demo|prod) is the **L2 control-plane tier**, not the stamp's
  environment; `customer.bicep:59` accepts dev|staging|prod.
- **`secretsVer` / `bicepVer` / `indexVer`**: documented as artifact versions (manifest hash, Bicep git SHA, index
  schema version) but computed nowhere; L2 ships those artifacts and could compute them (H12a already hashes its own
  manifest — `FileSeedManifestReader.cs:65`).
- **DAG ordering gaps**: H6 reads `BffAppRegId` (H3 output) but depends only on H5 (`DagAdvancer.cs:152`); H7 reads
  `SpeContainerId` (H8 output) but depends only on H6 (`:153`).
- **InterStepState with no writer**: `ContainerTypeId` (doc says H10 writes it; nothing does — H13 falls back to
  NonSecret), `SpeConsentCorrelationId`, `S2SAppRegId`. `FicPendingPostAppServiceVerification` is written by H3 and
  read by nothing (the "H13 must discharge it" contract is not implemented).
- **Second channel, same defect — `run.Parameters.Secrets` (H3 ↔ H4 deadlock)** (found 2026-10-01 while filing
  T245a). H4's manifest resolver reads `KeyVaultSecretRef`s from `run.Parameters.Secrets` for `TenantId`,
  `BFF-API-ClientId`, `BFF-API-Audience` (`from-run-parameter`) and `BFF-API-ClientSecret` (`from-existing-kv`;
  omitted when secret-free) (`KvSecretValueResolver.cs:231-250`, `manifest.yaml:126-235`). The only writer is H3, on
  consent-verified success (`H3EntraAppRegHandler.cs:653-659`) — and H3 depends on H4 (`DagAdvancer.cs:150`), so H4
  fails on every real run and a resume cannot unblock it. H3 already commits ClientId/Audience to the customer vault
  itself (`GraphAppRegistrationProvisioner.cs:223-227`); nothing supplies a `TenantId` ref at all (tenantId is a
  non-secret intake value). Fixed in T245a step 6b.
- H3 still builds a `BffClientSecretKvUri` reference and requires it non-blank even when the stamp is secret-free and
  no secret is written (`GraphAppRegistrationProvisioner.cs:233-242`, `H3:316`) — harmless today (only parsed for the
  vault name), but the reference names a secret that does not exist; revisit with G21 (secret-free default, T225b).
- H2a's globally-namespaced name check builds the Service Bus name `sprk-{id}-{env}-sb` (`H2a:647-665`), but
  `customer.bicep:177` names it `spaarke-{id}-{env}-sbus`.

## 2. Why it went unseen

Each handler task authored its inputs as `NonSecret` keys and left "upstream MUST populate" for later; the unit
tests then seeded those keys directly. No test asserts that a required input has a producer, and no run has yet
reached H2a against a real stamp (T186 is the first live E2E).

## 3. Proposed fix — T245 "run context: every handler input has exactly one producer"

**Rule**: every value a handler reads has exactly one declared source, and a test proves the source exists before
the reader runs.

| Source | Values | Mechanism |
|---|---|---|
| **Intake** (operator, validated at `POST /api/runs` → 400) | tenantId, subscriptionId, region, tier, cost fields, openAiLocation, containerTypeId; **new**: stamp environment (dev\|staging\|prod, default `prod` per D6), H11 users/preset, H14 operator-owned values | `intake.schema.json` + `CreateRun` validation + skill Step 1/4.0 |
| **Run identity** | customerId | `run.CustomerId` |
| **L2 platform configuration** (per L2 deployment, not per run) | SPE-certificate vault name (H0, H8); artifact versions `bicepVer` / `indexVer` / `secretsVer` | Options validated at Worker startup; versions computed from the artifacts L2 ships |
| **Handler outputs** (typed `InterStepState`, written once by the producer) | H2a → resource group, App Service, staging slot, Key Vault name + URI, UAMI resource id, Service Bus FQNS; H5 → Dataverse URL (H12b switches to it); H9 → BFF URL, build id | Map the existing ARM outputs; consumers stop reading `NonSecret` |

Plus:
- **H4b `per_env_settings` sources** become a closed catalog (source key → one of the sources above); an unknown
  key fails the manifest read — the same discipline T226 applied to `value_source`.
- **Forcing function**: one machine-readable input contract per handler, and a test that every required
  handler-output input is produced by a transitive DAG ancestor of its reader, every intake input is in the
  schema + CreateRun validation, and every configuration input is a validated option. This is the test that would
  have caught this gap.
- **DAG**: H6 ← H3, H7 ← H8.
- `keyVaultName` split into the platform SPE-cert vault (configuration) and the customer vault (H2a output).

Probably two tasks (split decided at `task-create`): **T245a** contract + test + H2a outputs + consumers + L2
configuration + DAG; **T245b** intake additions (schema, API validation, skill).

## 5. Status after T245a (2026-10-01)

**Fixed**
- H2a persists its ARM outputs (RG, App Service, slot, KV name + URI, UAMI resource id, Service Bus FQNS); H3, H4,
  H4b, H9, H12b, H13, H14 read them (and H5's `DataverseEnvUrl`) from typed `InterStepState`.
- `POST /api/runs` accepts only `IntakeParameterCatalog` keys; `environmentName` stored once (default `prod`),
  resolved the same way by H2a, H2b, H4b.
- H4b's `per_env_settings` sources are a closed catalog; the two mislabelled sources corrected.
- DAG: H4b ← H3, H6 ← H3, H7 ← H8.
- **Secrets channel**: `TenantId` from the intake value (`from-intake-parameter`); `BFF-API-ClientId` /
  `BFF-API-Audience` written by H3 itself (`written-by-h3`, H4 skips them); H3 no longer writes
  `run.Parameters.Secrets` (H4 runs before H3 and could never read them — the deadlock).
- T6 reads the SPE owner certificate from its own vault input, not the customer vault.
- H2a's name-availability check probes the Service Bus name the template creates (`spaarke-{id}-{env}-sbus`).
- Forcing function: `RunContextContractTests` (DAG ancestry, intake catalog, a Roslyn source scan of every handler
  folder — declared inputs equal reads, both ways — producer truthfulness, H4's manifest sources checked against
  `customer.bicep`'s `kvSecretValues`, the generator's per_env set equal to `PerEnvSourceCatalog`). Every rule has a
  seeded negative control and a positive control.
- Found and fixed by the T245a quality gates: H4's cleartext-leak guard flagged ordinary Azure host names (a real
  `CosmosEndpoint` is 44 characters of the token alphabet) and so would have quarantined every real run at H4 — it now
  exempts DNS hosts, still scans a URI's path / query / user-info, and covers every `InterStepState` string property;
  every control-plane ProblemDetails carries a stable `errorCode` (ADR-019); H13's `sprk_azuresubscriptionid` comes from
  the run's `subscriptionId` (the separate `azureSubscriptionId` intake key is gone); the `/provision-environment`
  skill's `Model2Dedicated` comparisons (dead since T223/T224 — the Model 2 hard stops never fired) now test `Model2`.

**Still open — pinned in `RunContextContractTests`, each with its owner**
| Gap | Owner |
|---|---|
| H0 / H8 / H13-T6 SPE owner-certificate vault (`keyVaultName` intake, interim) | T245b |
| `bicepVer`, `indexVer`, `secretsVer` (H2a, H2b, H4, H4b) | T245b |
| H13 `buildId`, `bffApiUrl`; H14 `webhookNotificationBaseUrl` (H9 outputs) | T245b |
| H7 `bffApiBaseUrl` — the stamp's own BFF URL (H9 output); today it falls back to the **platform** BFF `https://api.spaarke.com` | T245b |
| H13 registry columns `bffVersion` (H9), `solutionVersion` (H6), `clientCacheBustToken` (minted per deploy) | T245b |
| 🔒 H4's KV RBAC bootstrap grants Secrets Officer to the stamp UAMI (`MiObjectId`), not L2's own principal — not a pinned input but the same class: the right value (L2's principal id) has no source yet | T245b (owner review, security) |
| Manifest: `Dataverse-ServiceUrl` (H5 output, H5 not an ancestor of H4) | T245b |
| Manifest: `BingSearch-ApiKey`, `ContentSafety-ApiKey`, `LlamaParse-ApiKey` (Spaarke vendor keys → L2 config) | T245b |
| H11 `identityPreset`, `usersJson`; H14 `exchangePolicyScopeGroupId`, Graph resources | T245c |
| Manifest: `Communication-DefaultMailbox` (operator intake) | T245c |
| Manifest: `Dataverse-ClientSecret`, `BFF-API-ClientSecret` (`from-existing-kv` through the writer-less Secrets channel; secret-free default is G21) | T225b |
| Manifest: `SPE-DefaultContainerId`, `SPE-CommunicationArchiveContainerId` — labelled `from-bicep-output`, but `customer.bicep` cannot write them (H8 creates the containers after H4) | T227 (plan G18) |

A real run still cannot complete until those land; it now fails on a named, owned gap instead of on a value
nothing writes.

## 4. Effect on the plan

- **T238** (`Customer__Id` via H4b) depends on H4b being able to run at all; its `customer_id` source is one entry
  in the T245 catalog. Recommended order: T245a → T238 → T245b → rest of plan §7.
- **T186** (first live E2E) is blocked by this gap independently of every other gap in plan §4.
