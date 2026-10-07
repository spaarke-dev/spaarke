# T226 — shared-service secrets: verified read path + per-secret disposition

> **Task**: T226 (plan §7, gap G1). **Date**: 2026-09-30 SESSION 26. **Branch state**: post-master-merge `40c4b2a02`.
> Step 1 = read-only verification (this file §1–§3). Step 2 = proposed disposition (§4) — **owner approval required
> before any change** (POML constraint, root CLAUDE.md §6).

## 1. Where the six secrets are written and read today

| # | Fact | Evidence |
|---|---|---|
| a | **H4-shared writes to a SHARED vault named by run parameter `sharedKeyVaultName`**, extracting keys from services in run parameter `sourceResourceGroupName` (the `sprksharedprod-*` services). | `H4SharedKvSecretsPopulationHandler.cs:83-87, 235-248` |
| a' | **Nothing in the pipeline sets `sharedKeyVaultName` / `sourceResourceGroupName`** — only the retired `model1-shared.bicep` / `model1-customer.bicep` declare them. H4-shared fails `Resumable` when either is missing. | `git grep` over `src/`, `scripts/`, `.claude/skills/provision-environment/`, `provisioning-runs/`, `infrastructure/` |
| a'' | ⇒ **On every dedicated-customer run H4-shared fails**, and because `DagAdvancer.HandlerDependencies[H4b] = {H4, H4Shared}` and `[H9] = {H3, H4b}`, **H4b (app settings) and H9 (BFF deploy) never run.** Independent of the credential question, this is a Task-186 blocker. | `DagAdvancer.cs:160,163,168` |
| b | **H4b points every KV reference at ONE vault** — run parameter `keyVaultName` (the customer vault) — via the generated `Configure-AppServiceSettings.generated.ps1 -VaultName`. | `H4bBulkAppSettingsHandler.cs:62,188,312`; generated script `:74-78` |
| c | **Per-customer H4 skips all `from-shared-service` entries**; the resolver hard-fails if one reaches it. | `H4KvSecretsPopulationHandler.cs:464-471`; `KvSecretValueResolver.cs:193-199` |
| c' | **`customer.bicep` already writes 5 of the 6 into the customer vault from the customer's OWN resources**: Redis, Storage, DocIntel key always; AI Search admin key + Service Bus connection string only when `requireSecretFreeIdentity=false` (omitted, never sentinel, when true). It does **not** write `AzureOpenAI-ApiKey` (deliberate — BFF uses MI). | `customer.bicep:721-752` (post-merge line numbers may differ by a few) |
| d | **Customer UAMI RBAC**: Service Bus Data Sender + Receiver; AI Search Index Data Contributor + Service Contributor (`bff-runtime-rbac.bicep`); Cognitive Services User on OpenAI and DocIntel (`openai.bicep:167-168`, `doc-intelligence.bicep:51-52`). OpenAI (`kind: OpenAI`) and DocIntel (`kind: FormRecognizer`) both have `customSubDomainName` set. | modules cited |

**Live check**: no customer-shaped BFF created by a provisioning run exists (live App Services: `spaarke-bff-dev`,
`spaarke-bff-demo` (stopped), `sprksharedprod-api` (retired shared tier, stopped 2026-09-30)). POML escalation trigger 1
does **not** fire.

## 2. How the BFF authenticates to each service (code)

| Service | BFF selection logic | MI possible without a BFF code change? |
|---|---|---|
| Redis | Parses a connection string (`CacheModule.cs:59-97`); **no Entra-auth path** | ❌ access key required |
| Service Bus | MI when `ServiceBus:FullyQualifiedNamespace` is set, else connection string (`ServiceBusClientFactory.cs:83-96`). H4b sets the FQNS. | ✅ already MI |
| AI Search | MI when `AiSearch:ManagedIdentity:Enabled=true` (H4b sets it) or key absent; **all** consumers go through `SearchClientFactory` (`SearchClientFactory.cs:60-84`; `DataverseIndexSyncService.cs:82-92`; `AnalysisServicesModule.cs:1663-1670`; `RecordMatchService.cs:48`) | ✅ already MI |
| Azure OpenAI | Key if `AzureOpenAI:ApiKey` / `DocumentIntelligence:OpenAiKey` non-empty, else MI (`AiModule.cs:118-138`; `OpenAiClient.cs:84-113`) | ✅ by config — see §3 hazard |
| Document Intelligence | Key if `DocIntelKey` non-empty, else MI (needs custom subdomain + Cognitive Services User — both present) (`TextExtractorService.cs:72-97`) | ✅ by config |
| Storage | **No runtime consumer** of `ConnectionStrings:Storage` anywhere in `src/` (the session blob store uses endpoint + MI) | n/a — secret is unused |

## 3. 🔴 Hazard found: an unresolvable KV reference is sent as a credential

App Service uses the **literal** `@Microsoft.KeyVault(...)` string when a reference cannot resolve. H4b always sets
`AzureOpenAI__ApiKey` and `DocumentIntelligence__OpenAiKey` to a reference to `AzureOpenAI-ApiKey`, which **no writer
puts in the customer vault** (customer.bicep omits it; H4 skips `from-shared-service`; H4-shared writes the SHARED
vault). The BFF sees a non-empty "key", takes the key path, and every OpenAI call fails 401. `customer.bicep:561-566`
warns about exactly this; H4b re-introduces it. (The same literal-string effect on `ServiceBus__ConnectionString` /
`AiSearch__ApiKeySecretName` on secret-free stamps is harmless, because the FQNS / `ManagedIdentity:Enabled` settings
select MI first.)

## 4. Proposed disposition (Step 2 — for owner approval)

| Secret | Proposed | Manifest `value_source` | Writer | Change vs today |
|---|---|---|---|---|
| `Redis-ConnectionString` | **Key from the customer's own Redis** (BFF has no MI path) | `from-bicep-output` | `customer.bicep` kvSecrets (already) | manifest label only |
| `ServiceBus-ConnectionString` | **MI** (already selected via FQNS); secret written from the customer's own namespace only on non-secret-free stamps | `from-bicep-output` (stays a secret-free omit target) | `customer.bicep` (already) | manifest label only |
| `AiSearch--AdminKey` | **MI** (already selected); secret written from the customer's own service only on non-secret-free stamps | `from-bicep-output` (stays a secret-free omit target) | `customer.bicep` (already) | manifest label only |
| `DocumentIntelligence-ApiKey` | **Key from the customer's own DocIntel** (works today; MI is a later config toggle) | `from-bicep-output` | `customer.bicep` (already) | manifest label only |
| `Storage-ConnectionString` | **Keep as-is, relabelled** — unused at runtime; retiring it touches Model 2 Bicep + rotation scripts, so it becomes a follow-up (T235/T241 list) | `from-bicep-output` | `customer.bicep` (already) | manifest label only |
| `AzureOpenAI-ApiKey` | **DECISION** — (A, recommended) **MI**: remove the entry, so H4b stops emitting `AzureOpenAI__ApiKey` / `DocumentIntelligence__OpenAiKey`; E-2 fallback = operator adds a key + setting by hand if MI 401s on a customer account. (B) **Key from the customer's own OpenAI**: `customer.bicep` writes `listKeys()` into the vault (`from-bicep-output`); BFF uses the key (ADR-028 E-2 scope). | A: removed · B: `from-bicep-output` | A: none · B: `customer.bicep` | A: manifest + H4b settings · B: + customer.bicep |
| `PromptFlow-Endpoint`, `PromptFlow-Key` | **Remove** (D5) — also drops H4b's `Analysis__PromptFlow*` settings | removed | — | manifest |
| `BingSearch-ApiKey`, `LlamaParse-ApiKey` | Unchanged; purpose text labels them **Spaarke-shared vendor keys** (D5) | `from-run-parameter` | H4 | text only |

**Why A for OpenAI**: it is what `customer.bicep` already chose; ADR-028's canonical path is MI; E-2's 401 evidence is
from the dev account of `kind: AIServices`, whereas the customer module deploys `kind: OpenAI` with a custom subdomain
(E-2 is unproven there, not disproven). Fallback is config-only. **Risk**: if E-2 does reproduce, AI features on the
first customer stamp fail until the operator applies the fallback — H13 acceptance should exercise one OpenAI call
(check during T230).

**Consequence for code (either option)**: with no `from-shared-service` entries left, **H4-shared, its DAG edge,
its handler id / DI / dispatch registration, the shared accessor, source-key extractor and `SharedKvSecretSource`
types, and the `FromSharedService` value-source are retired** (H4b depends on H4 only). No new extraction code is
needed — the POML anticipated extending H4 with extraction; that is unnecessary because `customer.bicep` already
writes the values.

**Not changed by T226 (observations)**: `customer.bicep` `requireSecretFreeIdentity` defaults to `false`, so today
new stamps DO get the AI Search admin key + Service Bus connection string; whether new Model 1 stamps are run
secret-free is an H2a parameter question (note for T225b).

## 5. Owner decision (2026-09-30)

- **`AzureOpenAI-ApiKey` → A: managed identity** (approved). Remove the manifest entry so H4b stops emitting
  `AzureOpenAI__ApiKey` / `DocumentIntelligence__OpenAiKey`; E-2 fallback is an operator config action; H13 should
  exercise one OpenAI call (T230).
- **Other five + Prompt Flow / vendor labels → "Discuss first"**, then (owner, same day):
  - **Service Bus → remove from the process** (key not required; BFF uses MI via FQNS).
  - **AI Search → remove from the process** (key not required; all BFF consumers use `SearchClientFactory` MI; H2b
    uses the L2 identity via `DefaultAzureCredential`, not the admin key).
  - **Document Intelligence → MI** (remove the key).
  - "Remove from the process" = delete the manifest entry (H4b stops emitting the settings) **and** remove the key from
    `customer.bicep`'s `kvSecretValues` **and** its app-setting KV refs (`ConnectionStrings__ServiceBus`,
    `AI_SEARCH_API_KEY`, `DOC_INTELLIGENCE_KEY`) — a reference to a secret no longer written would reach the BFF as a
    literal string. `customer.bicep` is also the Model 2 template; MI behaves identically there (D3: not broken).
  - **Redis**: owner asked how it works today → answered (access key in a connection string, fetched from KV by the App
    Service's MI; Redis itself authenticates the key; no Entra path in BFF code or `redis.bicep`). Decision pending.
  - **Storage**: owner asked what it is → answered (connection string for `sprk{customerId}prodsa`; no reader). Decision pending.

## 6. Correction + quality gates (2026-09-30, end of SESSION 26)

**Correction to §2**: Document Intelligence MI is **not** reachable by config. `TextExtractorService.cs:743`
(`ExtractViaDocIntelAsync`) and `:967` (`ExtractLayoutAsync`) return "not configured" unless `DocIntelKey` is set, so
`CreateDocIntelClient`'s MI branch (`:76-97`) is never used. §2 cited only `:72-97`. AI Search: the BFF already selects MI,
but `modules/ai-search.bicep` creates the service keys-only → 403 on new stamps (pre-existing, G16).

**Owner decision D13 (keyless stamps)** → **T226 keeps `DocumentIntelligence-ApiKey`** (interim; removed by T243 once
the BFF MI path works). Everything else in §5 stands.

**Gates**: adr-check — compliant on ADR-028 A4 + lifecycle rules, ADR-027, ADR-032, ADR-010, §10 (no BFF change), §11,
ADR-038, package removal; 1 violation = DocIntel (V1, resolved by keeping the key). code-review — 1 critical (same
DocIntel), warnings + suggestions below.

### Remaining T226 work (next session) — apply ALL, then close

1. **Restore `DocumentIntelligence-ApiKey`**: manifest entry (copy from `git show HEAD:scripts/canonical-secret-catalog/manifest.yaml`,
   but `value_source: "from-bicep-output"`, no `service_ref`; keep `app_settings: DocumentIntelligence__DocIntelKey`;
   purpose notes "interim — removed by T243 (D13)"); `customer.bicep` `kvSecretValues` re-add
   `'DocumentIntelligence-ApiKey': docIntelligence.outputs.docIntelligenceKey` (do NOT restore the legacy
   `DOC_INTELLIGENCE_KEY` app setting — the BFF never read it); update header comments (Resolvable 6→7, removed-list);
   remove `DocumentIntelligence-ApiKey` from `FileKvSecretManifestTests` "removed keys" theory and add it as a
   `FromBicepOutput` mapping case; regenerate (`Invoke-CatalogGenerator.ps1`, then `-Verify`); `az bicep build` customer.json.
2. **Review fixes**:
   - `StaticKvSecretManifest.cs:74,83,89` (emergency DI fallback) — align with the manifest (drop `AiSearch--AdminKey`,
     `AzureOpenAI-ApiKey` [currently `Generated` = random hex!]; `SPE-ContainerTypeId` → `FromTopologyConstants`) or delete
     the class if unreferenced.
   - `Model1SharedDagParityTests.cs:86-97,151` — remove the 9 stale `"H4-shared"` completed-phase rows + "4-way"/"H4b
     unlocks" labels.
   - `DagAdvancerTests` — delete `ComputeReadyHandlers_H4SharedIsNotADagParticipant` (ADR-038 B6 mirror test; HANDLER-12
     parity already covers it); fix header numbering (5→7 gap); comment at :390.
   - `KvSecretValueResolver.cs:200-204` — the "GENERATE branch" doc comment now sits on `ResolveTopologyConstant`; move it
     back to `ResolveGenerated`. Diagnostic: drop "Add the parameter to the run and resume" (no such path) → "provide
     containerTypeId at intake (the /provision-environment skill injects it from spaarke-constants.yaml)".
   - `H4KvSecretsPopulationHandler.cs:468` — `entries.ToList()` → use `entries` directly. `BuildTopologyConstantValues`:
     `Trim()` the value.
   - Add test: reader rejects an unrecognized value_source (e.g. `from-shared-service`) via `ParseYamlForTest`.
   - Stale "H4-shared" comments (current-state, not history): Worker `Program.cs:522,537,568`;
     `ArmOperatorKvRbacBootstrapper.cs:100,121`; `IOperatorKvRbacBootstrapper.cs:9,14`; `ISecretFreeMarkerApplier.cs:5,29,37`;
     `ArmSecretFreeMarkerApplier.cs:17`; `SecretFreeMarkerConsistencyDetector.cs:28-30`; `BulkAppSettingsRejectionCodes.cs:6`;
     `IPerEnvSettingsManifest.cs:31`; `FileKvSecretManifest.cs:62-66` (3 omit targets); `IKvSecretManifest.cs:67,102`;
     `customer.bicep:736-739` (SPE-ContainerTypeId is from-topology-constants, not FromRunParameters) + double blank ~:620;
     tests `H4bBulkAppSettingsHandlerTests.cs:6,697`, `ArmOperatorKvRbacBootstrapperTests.cs:276`,
     `H4KvSecretsPopulationHandlerTests.cs:1014`, `RunsEndpointsTests.cs:842` ("Ten" → "Nine").
   - `.claude/**` drift (MAIN SESSION ONLY): `constraints/provisioning.md:88` (21/21 → 20/20) + `:119` (H4-shared as a
     drift-detection handler); `patterns/provisioning/handler-registration-completeness.md:37,105` (21/21);
     `patterns/provisioning/manifest-driven-secret-catalog.md` (describes H4-shared — add a retirement banner + fix the
     value_source list); `skills/provision-environment/SKILL.md:42,226,231,1125,1131,1223`; add a root `.claude/CHANGELOG.md`
     entry for these.
   - `scripts/canonical-secret-catalog/README.md:72` — add `from-topology-constants`, drop `from-shared-service`.
3. **Completion notes must record**: next blockers G16–G20 (plan §4); §7.9 pre-check = no live stamp was built from
   `customer.bicep` (live App Services: spaarke-bff-dev, spaarke-bff-demo, sprksharedprod-api — none customer-shaped), and
   T226 deletes no live KV secret; publish size N/A (no BFF code/package change); ADR Tensions: Redis key is a non-MI
   outbound not on ADR-028's exception list (T242), OpenAI key removed under E-2 by owner decision (T230 measures);
   orphan risk: a queued/crash-recovered "H4-shared" message dead-letters as NoHandler (harmless); T221 — 17 of its 23
   baseline failures (FileKvSecretManifestTests) are FIXED by T226, 6 CustomerRunGuard remain.
4. **Verify + close**: builds (ControlPlane Api/Worker/Tests, Sprk.Bff.Api) 0/0; ArchTests 337/337; ControlPlane.Tests
   only the 6 CustomerRunGuard failures; generator `-Verify` OK; re-run code-review + adr-check (quick) on the delta;
   POML → completed + completion-notes; TASK-INDEX 226 ✅ (+ T221 note); plan §3 (manifest bullet ✅) / §4 G1 ✅;
   **ONE commit** with all T226 code (incl. the 8 deleted files) — beware: `git commit` takes everything staged;
   push.

## 7. Completion record (2026-09-30, SESSION 27)

**Outcome against the goal.** No provisioning run writes, and no customer BFF reads, a credential or endpoint of a
`sprksharedprod-*` resource. `from-shared-service`, H4-shared and its DAG edge are gone (H4b ← H4 only). Final
per-secret state (owner §5 + D13):

| Secret | State after T226 | Writer | Follow-up |
|---|---|---|---|
| `ServiceBus-ConnectionString` | removed — BFF uses the stamp UAMI (FQNS) | — | — |
| `AiSearch--AdminKey` | removed — BFF uses the stamp UAMI | — | G16 / T244 (service is keys-only today) |
| `AzureOpenAI-ApiKey` | removed — BFF uses the stamp UAMI | — | T230 measures E-2 on a customer account |
| `Storage-ConnectionString` | removed — no reader | — | — |
| `PromptFlow-Endpoint` / `-Key` | removed (D5) | — | — |
| `DocumentIntelligence-ApiKey` | **kept, interim** (D13) — customer's own DocIntel | `customer.bicep` | T243 removes |
| `Redis-ConnectionString` | **kept, interim** — customer's own cache | `customer.bicep` | T242 (Azure Managed Redis, Entra-only) |
| `BingSearch-ApiKey`, `LlamaParse-ApiKey` | unchanged; labelled Spaarke-shared vendor keys (D5) | H4 (run parameter) | — |

**Also fixed (found during T226).**
- Production defect: the C# manifest reader and the generator rejected task 214's `from-topology-constants`, so
  **every H4 run failed `ManifestReadFailed`**. Reader, enum (`FromTopologyConstants = 6`), resolver and generator
  now agree; H4 passes the run's `containerTypeId` (trimmed). This is the root cause of 17 of the 23 task-221
  baseline failures (`FileKvSecretManifestTests`) — all 17 now pass; the 6 `CustomerRunGuardModulePostConfigureTests`
  remain (T221).
- Generator duplicate-key order: `AzureAd__TenantId` / `AzureAd__ClientId` are emitted twice (a secret's KV
  reference and a `per_env_settings` value); `az webapp config appsettings set` keeps the last one, and the
  unstable `Sort-Object` let T226's line removals flip TenantId to the KV reference. The sort now breaks ties by
  insertion order (KV refs, then per-env values), so the per-env value always wins — the pre-T226 output.
- Scope note (adr-check W3): the `from-topology-constants` fix adds code inside existing files
  (`KvSecretValueSource.FromTopologyConstants`, `KvSecretWriteRequest.TopologyConstantValues`,
  `H4KvSecretsPopulationHandler.BuildTopologyConstantValues`, a resolver branch). It is in scope because without
  it H4 cannot run at all; no new type or file.
- `StaticKvSecretManifest` deleted — unregistered, and its list had drifted (it served `AzureOpenAI-ApiKey` as a
  random `Generated` value).
- `prereqs.yaml` PRQ-E-06 retired (it granted the L2 identity key-reading roles on the shared services for
  H4-shared). `validate.ps1` accepts `status: retired` entries without a recipe and the skill's Step 0.5b skips
  them; PRQ-E-13 is scoped `once_per_customer`. The `provisioning-prereqs-validate` CI check (runs on every PR and
  on master) was **red before T226** on PRQ-T-07 + PRQ-E-13 and is green now.
- Docs that described the retired flow: `.claude/patterns/provisioning/manifest-driven-secret-catalog.md`
  (rewritten against the code), `handler-registration-completeness.md`, `.claude/constraints/provisioning.md`,
  `provision-environment` SKILL, `PROVISIONING-PREREQUISITES.md`, catalog README; `.claude/CHANGELOG.md` entry.

**Quality gates — second round on the delta (code-review + adr-check, 2026-09-30).** No Critical findings.
Applied:
- 🔴 `/provision-environment` Step 4.0 never sent `containerTypeId` (Step 0.5b read it; H8's own diagnostic says
  Step 4.0 populates it) → every run would fail H4's `SPE-ContainerTypeId` write and H8. The payload now sends it.
- Generator duplicate-key tie-break (above); H8 trims `containerTypeId` like H4 (test added); a test pins H4's
  topology-constant map to the manifest (both directions); the redundant H4b DAG test was merged into the
  exact-set test.
- Doc accuracy: the resource inventory rows; `handler-registration-completeness.md` and
  `.claude/constraints/provisioning.md` corrected against the code (`AddKeyedScoped` forwarders, `HandleAsync`,
  `Success`/`Failure(FailureClass,…)`, `Dispatchable` + `HandlerDependencies` steps, `NoHandler` dead-letter, no
  `HandlerIdempotencyTests` class); `customer.bicep` AI Search G16 caveat; `intake.schema.json`; prerequisites guide
  headings; `IKvSecretManifest` `FromBicepOutput` doc; `.claude/CHANGELOG.md`.
- ADR-028 tension row added to `spec.md` § ADR Tensions (Redis + DocIntel keys, Path A, owner D13).
- New plan gap **G21** (secret-free default for new stamps → T225b).
Not applied (recorded): unused `OPENAI_ENDPOINT` / `AI_SEARCH_ENDPOINT` / `DOC_INTELLIGENCE_ENDPOINT` settings and
the modules' key outputs in `customer.bicep` (T244 owns the secret-bearing outputs); `ContentSafety-ApiKey` belongs on
the D13 backlog; `IKvSecretManifest` stays a seam (one production implementation + the H4 test double — the
project's convention).

**Final verification.** Builds 0 warnings / 0 errors (ControlPlane Api, Worker, Tests; Sprk.Bff.Api).
ControlPlane.Tests 1937 passed / 6 failed (the `CustomerRunGuardModulePostConfigureTests` baseline only) / 1 skipped.
ArchTests 337/337. Generator `-Verify` OK. `prereqs.yaml` validator OK. `customer.json` = fresh `az bicep build`.
Zero `sprksharedprod` / `from-shared-service` in the manifest and generated artifacts. Zero files under
`src/server/api/Sprk.Bff.Api/**` changed.

**Pre-checks and measurements.**
- §7.9 pre-check: no live App Service was built from `customer.bicep` (live: `spaarke-bff-dev`, `spaarke-bff-demo`,
  `sprksharedprod-api` — none customer-shaped). T226 deletes no live Key Vault secret; it changes what a future
  stamp writes. POML escalation trigger 1 did not fire.
- BFF publish size: N/A — no file under `src/server/api/Sprk.Bff.Api/**` changed, no package added.
- Model 2 (D3): `customer.bicep` is shared by both models; MI behaves identically there. No Model 2 test or DAG edge
  depended on H4-shared.
- Orphan risk: a queued or crash-recovered `H4-shared` message would dead-letter as `NoHandler` — harmless, and no
  live run exists.

**ADR tensions (CLAUDE.md §6.5) — documented, owner-decided** (row added to `spec.md` § ADR Tensions).
- ADR-028 (MI for outbound calls; exceptions E-1, E-2 only): the Redis key and the Document Intelligence key are
  non-MI outbound credentials not on the exception list. Path: keep as an **interim, time-boxed deviation** under
  owner D13 (keyless stamps implemented incrementally), removed by T242 / T243; the ADR-028 amendment that records
  D13 is T235.
- ADR-028 E-2 (OpenAI 401 under MI on the dev `AIServices` account): the customer stamp uses MI for OpenAI by owner
  decision; T230 exercises one OpenAI call on the first stamp.

**Next blockers before T186 (plan §4):** G16 (AI Search keys-only + L2 Search role), G17 (→ T243), G18 (SPE
container ids labelled `from-bicep-output`, unwritten → H4 quarantine), G19 (missing `containerTypeId` →
QuarantineRequired, no way to add it to a run), G20 (Model 1 still deploys `model1-shared` → T225b), **G21 (new;
adr-check W5)** — `RequireSecretFreeIdentity` defaults `false` everywhere, so H4 serves `BFF-API-ClientSecret` on a
new stamp → T225b. `customer.bicep` still outputs the Storage + Service Bus connection strings (read only by the
deprecated `Provision-Customer.ps1`); dropping them is already in T244.

**Observation (not fixed):** `scripts/provisioning-prereqs/validate-recipe-authoring.ps1` is not wired into CI and
fails on most recipes (missing emptiness guards) — predates T226.
