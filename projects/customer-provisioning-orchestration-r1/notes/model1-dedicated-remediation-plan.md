# Model 1 Dedicated-Stamp Remediation Plan

> **Created**: 2026-09-30 (SESSION 25, after T224)
> **Status**: ACTIVE — the authoritative remaining-work plan for this project. Supersedes the
> "BINDING sequence" list in `current-task.md` where they differ.
> **Inputs**: owner decisions 2026-09-30 (§2) · `INCOMING-D12-D13-REMEDIATION.md` from
> `unified-access-control-r2` (§5 Items 1–4, §6, §7) · scope assessment 2026-09-30 (§4).
> **Why this exists**: the INCOMING doc is a *contradiction sweep* ("what in your tree contradicts
> D-12/D-13"), not a forward design of per-customer Model 1 provisioning. The 2026-09-30 assessment
> found gaps it did not list (§4). This plan unifies both into one task list.

---

## 1. Target model (owner-confirmed 2026-09-30)

| | **Model 1** (in scope) | **Model 2** (deferred — see §6) |
|---|---|---|
| Hosting tenant | Spaarke tenant | Customer tenant |
| Dataverse environment | Dedicated, Spaarke Power Platform tenant | Dedicated, customer tenant |
| Dataverse solutions | **Managed** (trial/sandbox may use unmanaged — special case, out of scope) | Managed |
| Azure subscription + RG | **Dedicated per customer**, Spaarke-owned + Spaarke-billed | Dedicated, customer-owned |
| SPE container type | `Spaarke Model 1` (`standard` — **Spaarke pays**) | `Spaarke Model 2` (`directToCustomer`) |
| SPE containers | Spaarke tenant (customer-tenant variant deferred — §6) | Customer tenant |
| Customer users | **B2B guests in Spaarke tenant; Spaarke pays licenses** | Native customer users |
| Email | Graph API, configured per customer **outside this project** | same |
| Customer environments | **prod only** (`{env}` = `prod`) | same |

**Dedicated vs shared rule**: every Azure resource the customer's BFF uses lives in the customer's own
subscription/RG → **dedicated**. **Shared** = Spaarke-operated, serves many customers, no customer data
requiring segregation: the L2 control plane, the admin/registry Dataverse env, BFF build artifacts + ACR,
SPE container types + owning apps, Office add-in SWA + Teams app packages, and third-party vendor accounts
(Bing, LlamaParse — §2 D5).

### Per-customer resource inventory (Model 1, `{env}` = `prod`)

| Area | Resource | Naming | Created by |
|---|---|---|---|
| Prereq (manual) | Azure subscription | operator-chosen (manual prereq, D4) | Operator |
| Prereq (manual) | Dataverse environment | operator-chosen (manual prereq, D4) | Operator / H5 — see §5 Q1 |
| Entra | BFF app registration (D-13) | `Spaarke BFF - {CustomerName}` | H3 |
| Entra | FIC on BFF app-reg | `spaarke-uami-trust` | H3 |
| Entra | UAMI | `mi-spaarke-{customerId}-prod` | H2a |
| Entra | Graph app-role grants on UAMI (~15) | per `GraphAppRoles.cs` | H10 |
| Dataverse | Managed solution package | **to be defined by T218** | H6 |
| Dataverse | 7 env-var values | `sprk_BffApiBaseUrl` … `sprk_SharePointEmbeddedContainerId` | H7 |
| Dataverse | App users: BFF app-reg + UAMI (SysAdmin, Root BU) | — | H10 |
| Dataverse | B2B guest users + licenses (Spaarke-paid) | — | H11 |
| Azure | Resource group | `rg-spaarke-{customerId}-prod` | H2a |
| Azure | Key Vault | `sprk-{customerId}-prod-kv` | H2a / H4 |
| Azure | Log Analytics / App Insights | `sprk-{customerId}-prod-logs` / `-insights` | H2a |
| Azure | Storage | `sprk{customerId}prodsa` | H2a |
| Azure | Service Bus (+4 queues, `membership-events`/`recon`) | `spaarke-{customerId}-prod-sbus` | H2a |
| Azure | Cosmos DB (`spaarke-ai`) | `spaarke-{customerId}-prod-cosmos` | H2a |
| Azure | Azure OpenAI (westus3, 4 pinned deployments) | `sprk-{customerId}-prod-openai` | H2a |
| Azure | AI Search (7 canonical indexes) | `sprk-{customerId}-prod-search` | H2a / H2b |
| Azure | Document Intelligence | `sprk-{customerId}-prod-docintel` | H2a |
| Azure | Redis **Standard C1** | `sprk-{customerId}-prod-redis` | H2a |
| Azure | App Service Plan (S1) + BFF App Service + staging slot | `sprk-{customerId}-prod-plan` / `-api` | H2a / H9 |
| Azure (opt) | SignalR / ACS | `-signalr` / `-acs` | H2a (flag-gated) |
| SPE | Container-type registration grant for the customer's BFF app-reg | on `Spaarke Model 1` registration (Spaarke tenant) | T227 — TBD handler |
| SPE | Default + communication-archive containers | displayName = customer name | H8 |
| Registry | `sprk_dataverseenvironment` row (admin env) | keyed by `customerId` | H13 / L3 skill |

---

## 2. Owner decisions (2026-09-30) — LOCKED

| # | Decision |
|---|---|
| D1 | Model 1 SPE container type = `Spaarke Model 1`, billed to Spaarke. |
| D2 | Model 1 customer users are B2B guests in the Spaarke tenant; Spaarke pays licenses. Email = Graph API, configured separately (out of this project). |
| D3 | **Model 2 is out of scope** for this project — a variant of Model 1 to address when a Model 2 customer arrives. No new Model 2 work; do not break existing Model 2 code paths. |
| D4 | Azure subscription + Dataverse environment naming (and creation) are **manual prerequisites** of the orchestration run. |
| D5 | Third-party vendor services (Bing, LlamaParse) use **Spaarke shared accounts**; revisit per-customer later on cost. Prompt Flow is dead (BFF readers removed 2026-08-21) — remove from catalog. |
| D6 | Customer stamps are **prod only**; customer-specific staging/dev deferred. |
| D7 | Dataverse solution package must be **defined** (solutions + components), not assumed from the existing deploy list. |
| (prior) | T223/T224: enum `Model1`/`Model2`; §9 Q1 Power BI F-SKU deferred (placeholder only); §9 Q2 Copilot agent per customer; §9 Q3 Redis Standard C1, verify empirically after first customer. |

---

## 3. Status of `unified-access-control-r2` INCOMING items

| INCOMING item | Status | Where |
|---|---|---|
| §5 Item 1 — delete H3 shared app-reg branch (D-13) | ✅ T222 `1e586978f` | — |
| §5 Item 2 — one TenancyModel enum + parse-or-reject | ✅ T223 `31b648098` | — |
| §5 Item 3 — `sprk_tenancymodel` migrate/rename | ✅ T224 `e5e049335` (rename to `Model1`/`Model2`; greenfield → no data migration) | — |
| §5 Item 4 — retire `model1-*.bicep` (7 surfaces) | 🔲 | **T225a** |
| §6 `prereqs.yaml` (shared BFF / `rg-spaarke-shared` / `model1-shared.bicep`) | 🔲 | T225a + T228 |
| §6 `canonical-secret-catalog/manifest.yaml` | 🔲 | **T226** |
| §6 `Seed-PlatformKeyVault.ps1:402-403` `SharedPlatformOpenAiEndpoint` | 🔲 | T225b |
| §6 `controlplane-worker-app-service.bicep` Model1Shared config | 🔲 | T225b |
| §6 `bff-runtime-rbac.bicep` shared UAMI | 🔲 | T225a |
| §6 `controlplane-subscription-rbac.bicep` fleet-subscription assumption | 🔲 | **T228** |
| §7 DoD — docs corrected or explicitly deferred | 🔲 | T235 |
| §8 — `Secure Project`→`Secure Record`, 3 Redis cache-key sites | NOT OURS (uac-r2 owns) | — |

---

## 4. Gaps found by the 2026-09-30 assessment (not in INCOMING)

| # | Gap | Evidence | Task |
|---|---|---|---|
| G1 | **Shared-service secrets for every customer.** `DagAdvancer.cs:160` runs `H4SharedKvSecretsPopulationHandler` for all runs; H4b depends on it. `manifest.yaml` sources Redis, Service Bus, Storage, OpenAI, DocIntel, AI Search keys from `sprksharedprod-*`, although `customer.bicep` deploys all six per customer. **Unverified**: which KV the per-customer BFF actually reads — if shared, customers share Redis (OBO tokens). Verify first. | DagAdvancer.cs:160,163; manifest.yaml:269,287,306,419,452,495 | T226 |
| G2 | Shared BFF app-regs `Spaarke BFF - Trial 1` / `- Model 1` still defined | `Register-EntraAppRegistrations.ps1:1243-1272`; `spaarke-constants.yaml:101-124` | T227 |
| G3 | No per-customer subscription path for Model 1: intake exempts Model 1 from `subscriptionId` and auto-injects the shared sub; L2 UAMI RBAC assumes one fleet sub | `RunsEndpoints.cs` ISH-02; `intake.schema.json`; `controlplane-subscription-rbac.bicep` | T228 |
| G4 | Shared-tier cost model: H0 `shared-trial` tier; `Model1MarginalEnvelopeUsd` = marginal-on-shared-platform | `H0Options`, `H13AcceptanceOptions` | T229 |
| G5 | H13 acceptance for Model 1 still probes the shared-platform AI Search tenant-filter template | `AiSearchTenantFilterInvariantProbe` Model1 branch | T230 |
| G6 | Tenancy × profile pairing: validator pairs `Model1` ↔ `spaarke-hosted-model1-trial`; Item 3 fan-out says `spaarke-hosted-model2` → Model 1 (T224 code-review W1) | `RunsEndpoints.TryValidateTenancyProfilePair` | T225b |
| G7 | Redis Bicep default Basic C0 vs owner decision Standard C1 | `customer.bicep:114,117` | T225a |
| G8 | BFF's own `TenancyModel` enum still `Model1Shared`/`Model2Dedicated` — would not parse post-T224 Dataverse labels | `Sprk.Bff.Api/Services/Registration/DataverseEnvironmentRecord.cs:37` | T233 |
| G9 | SPE: per-customer BFF app-regs each need an `applicationPermissionGrants` entry on the `Spaarke Model 1` container-type registration; no handler owns it | topology doc §3A/§5; inventory doc row "Container-type registration" | T227 |
| G10 | H11: B2B invite flow exists; license assignment (Spaarke-paid) + Dataverse user sync for guests unverified | `GraphRestB2BConsentVerifier.cs` | T232 |
| G11 | Dataverse package is not defined in source: only `spaarke_insights` is unpacked; the 9 deployed "solutions" are prebuilt zips; `SpaarkeCorporateCounselApp` has no source folder; 36 `src/solutions` folders mostly code pages deployed outside H6; some listed solutions believed unused (e.g. `EventDetailSidePane` — still referenced by NavigatorPane/EventsPage/Notepad code; verify) | `Deploy-DataverseSolutions.ps1:170-191,435-438`; `CanonicalSolutionCatalog.cs` | T218 |
| G12 | Docs/governance describe shared Model 1: spec.md, design.md, project CLAUDE.md MUST rules, deployment guide, inventory doc, topology doc, root CLAUDE.md pointer rows, `/provision-environment` skill | — | T235 |
| G13 | T224 W7/W8 leftovers: `H0Options.RequireCostEnvelopeForModel2Dedicated`, `isModel2Dedicated`, `Model1SharedDagParityTests.cs` filename | T224 code-review | T229 / T225b |

---

## 5. Open confirmation (1)

- **Q1 — H5 vs D4**: D4 makes the Dataverse environment a manual prerequisite. Does the operator **create** the
  environment (H5 becomes verify/adopt-existing), or only **name** it at intake (H5 still creates)? Resolve before T228.

---

## 6. Deferred / out of scope

- Model 2 (D3) — existing code paths retained, untouched. Includes Lighthouse, H0.5 consent, `directToCustomer` container type.
- Model 1 variant with SPE containers in the customer's tenant (needs multi-tenant BFF app-reg + consent + registration in customer tenant).
- Trial / sandbox environments (unmanaged solutions) — special case.
- Customer staging/dev stamps (D6).
- Per-customer vendor accounts (D5).
- Email/Graph mailbox configuration (D2).
- Power BI F-SKU — placeholder note only (§9 Q1).

---

## 7. Remediation task list (proposed IDs — file via `task-create` next session)

| ID | Title | Scope | Depends |
|---|---|---|---|
| **T236** | Inventory doc → dedicated Model 1 target | Rewrite `SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md` from §1 (scope target for all tasks below) | — |
| **T226** | Shared-service secrets → dedicated (G1) | **Step 1: verify** which KV/app-settings the per-customer BFF reads. Then: `manifest.yaml` 6 entries `from-shared-service` → per-customer (Bicep outputs of the customer's own resources); regenerate generated files; retire `H4SharedKvSecretsPopulationHandler` + DAG edge (H4b ← H4 only) + shared accessor/extractor types; remove Prompt Flow entries (D5); Bing/LlamaParse stay Spaarke-shared (D5) | T236 |
| **T225a** | Item 4 — retire `model1-*.bicep` (7 surfaces) | Per INCOMING §5 Item 4 (stacks, orphan, l2-rbac module, 4 bicepparams, `bicep-e2e-dry-run.ps1` Assertion 8, ARM-artifact workflow + manifest schema) + `bff-runtime-rbac.bicep` shared UAMI + `prereqs.yaml` shared entries + Redis default Standard C1 (G7). Touches `.github/workflows/**` (r1-owned per project CLAUDE.md) | — |
| **T225b** | Converge Model 1 onto the dedicated code path | H2b `HandleModel1BranchAsync` + tenant-filter template → dedicated; H12c `SharedPlatformOpenAiEndpoint` branch + `RuntimeReferencesOptions` + `Seed-PlatformKeyVault.ps1:402-403` + worker bicep config; ArmDeploymentRunner/FileBicepTemplateInspector → `customer` template for both; tenancy × profile pairing (G6); `Model1SharedDagParityTests` rename (G13) | T225a |
| **T227** | Entra + SPE topology for per-customer Model 1 | Retire shared Trial1/Model1 BFF paths in `Register-EntraAppRegistrations.ps1` + `spaarke-constants.yaml` (G2); per-customer `applicationPermissionGrants` on `Spaarke Model 1` registration (G9 — verify grant limits; decide handler: H8 pre-step or new); topology doc §3A | T225b |
| **T228** | Subscription + Dataverse env as manual prerequisites (D4, G3) | Intake requires `subscriptionId` for Model 1 (remove shared-sub auto-inject + ISH-02 exemption); H1 SpaarkeOwned path on per-customer sub; L2 UAMI RBAC per customer subscription (replace fleet-sub `controlplane-subscription-rbac.bicep` assumption — grant as part of the manual prereq); H5 per Q1; `prereqs.yaml` + L3 skill intake + operator runbook step | Q1, T225a |
| **T229** | Cost model for dedicated stamps (G4, G13) | H0 tiers (drop `shared-trial` for Model 1); H13 envelopes (`Model1MarginalEnvelopeUsd` → dedicated-stamp envelope); intake `costEnvelopePolicy` text; `RequireCostEnvelopeForModel2Dedicated` rename | T225b |
| **T230** | H13 acceptance for dedicated Model 1 (G5) | I2 probe Model1 branch → dedicated; audit other H13 verifiers for shared-platform assumptions | T225b |
| **T232** | H11 B2B guests + Spaarke-paid licensing (D2, G10) | Verify/implement license assignment + guest → Dataverse user sync in the Spaarke-tenant env | — |
| **T233** | BFF `TenancyModel` enum rename (G8) | `Sprk.Bff.Api` Registration enum → `Model1`/`Model2`; BFF §10 checks (publish size, tests) | — |
| **T218** | Define the complete Dataverse solution package (D7, G11) + managed-solution runbook + IAM + UPDATE audit | Design the solution set + components from scratch (not the existing zip list); decide which code pages ship in which solution; retire unused (verify `EventDetailSidePane` refs); source-control the definitions; update H6 `CanonicalSolutionCatalog` + `Deploy-DataverseSolutions.ps1`; include per-customer Copilot agent (§9 Q2) | T236 |
| **T235** | Docs + governance (G12, INCOMING §7) | spec.md, design.md, project CLAUDE.md MUST rules + ADR Tensions, deployment guide, topology doc, root CLAUDE.md pointer rows (+ `.claude/CHANGELOG.md`), `/provision-environment` skill (main-session only), Power BI placeholder note | T225b–T230 |
| T221 | 23 baseline test failures | unchanged — any time | — |
| 213.7 / 207 / 208 / 209 | pre-existing blockers | SPE runbook + constants; placeholder substitution; validator CI wiring; branch protection | T227 (213.7) |
| **T186** | First live E2E dispatch — Model 1 dedicated customer | after all above | all |

**Suggested order**: T236 → T226 (verification step first — highest-risk gap) → **master merge** (branch is 343 behind `origin/master` as of 2026-09-30; do it before T225a, which touches Bicep + `.github/workflows/**`; re-verify ArchTest 326/326) → T225a → T225b → T227 → T228 → T229 + T230 → T232 → T233 → T218 → T235 → pre-existing blockers → T186.

---

## 8. How to use this plan

- Next session: file T236, T226, T225a first via `task-create` (POMLs), then execute via `task-execute`.
- Each new task POML cites this file + the §2 decision rows it implements.
- Update §3/§4 status columns as tasks land.
