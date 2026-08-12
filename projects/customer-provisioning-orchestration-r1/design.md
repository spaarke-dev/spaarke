# Customer Provisioning & Deployment Orchestration — Design Specification

> **Status**: Draft v3 — post-2026-08-12 assessment refresh, pending owner review
> **Created**: 2026-06-15
> **Revised**:
> - 2026-06-16 (feedback round 1: resource inventory, identity spec, config capture, Q1–Q6 resolved)
> - 2026-08-12 (v3: D3 ADR-tension amendment, TF Power Platform provider adoption, H12 promoted to first-class config-seed manifest, silent-failure trap catalog, Cosmos DB provisioning added, SPE confidential-client fix, resolved v2 open items B1–B3/I1–I3/I5–I6)
> **Author**: Ralph Schroeder / Claude Code
> **Project**: customer-provisioning-orchestration-r1
> **Supersedes**: `projects/spaarke-environment-factory-r1/design.md`
> **Predecessors**: `spaarke-environment-provisioning-app` (r1, complete PR #390), Phase 0 discovery report (`discovery/phase-0-discovery-report.md`)
> **Companion docs (v3 authoritative supplements)**:
> - [`PROJECT-UPDATE-2026-08-12.md`](PROJECT-UPDATE-2026-08-12.md) — 2026-08-12 six-workstream assessment + design-refresh rationale + fast-follow list
> - [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) — machine-hardened bill-of-materials for one customer environment (386 solution components, 87+ entities, 33-PCF/7-in-use gap, Azure stamp, config/seed layer)
>
> Sections 7 / 8 / 11 of this design SUMMARIZE what those docs enumerate authoritatively; when they disagree, the companion docs win.

---

## 1. Executive Summary

Build the single, systematic process for standing up a new Spaarke customer environment and deploying the platform into it. One orchestrated pipeline, driven by a control-plane service with Cosmos DB state persistence, backed by idempotent deterministic handlers, all state-tracked against the `sprk_dataverseenvironment` registry.

When a paying customer is approved, the pipeline provisions a dedicated Dataverse environment, deploys the full managed solution, stands up per-customer isolated Azure resources (OpenAI, AI Search, Document Intelligence, Service Bus, Redis, Key Vault, App Insights, Storage, App Service), seeds configuration, wires post-deploy integrations, and verifies the result. The same package deploys into a customer's own tenant (Model 2) with target tenant as the only meaningful variable.

Spaarke already has most of the automation across three generations of provisioning assets. What it lacks is unification: the assets don't reference each other, five phases are still guided-manual, and nothing connects an environment's registry record to its actual provisioning state. This project unifies them.

---

## 2. Problem Statement

**Current state** — three fragmented generations:

| Generation | Assets | Character |
|---|---|---|
| Gen 1 (2026-03) | `ENVIRONMENT-DEPLOYMENT-GUIDE.md` (14 sections, 13 documented workarounds) | Validated but heavily manual; 13 known issues |
| Gen 2 (2026-03-05) | `Provision-Customer.ps1` (13 steps, idempotent, resumable), `customer.bicep` + 24 modules, `CUSTOMER-ONBOARDING-RUNBOOK.md`, `Decommission-Customer.ps1`, `Validate-DeployedEnvironment.ps1` | Strong automation, unaware of Gen 1 guide and Gen 3 registry |
| Gen 3 (2026-06) | `sprk_dataverseenvironment` entity (16 cols), registration/user-provisioning flow, `auth-deployment-setup.md` (auth v2, 21 MUSTs) | Registry exists but only the registration flow consumes it |

An operator standing up a new environment today must mentally merge three documents, decide which script generation applies, and execute five phases by hand. Nothing records which phase an environment has reached.

**Desired state**: one pipeline, one control plane, one skill. The registry record is created at provisioning start, tracks per-phase progress, and reaches `Setup Status = Ready` only when validation passes.

---

## 3. Locked Decisions

These are inputs, not proposals. The design conforms to them. D1-D11 from `discovery/phase-0-discovery-report.md` section 1; D12-D17 resolved in feedback round 1 (2026-06-16).

| # | Decision | Design implication |
|---|----------|--------------------|
| D1 | **Managed solutions** for customer environments. Unmanaged stays dev-only. | Solution export/fix pipeline must produce managed packages. |
| D2 | **One deployment package, two targets.** Variable = target tenant (Spaarke vs customer). Per-customer app registrations in both models. | Tenant is a run parameter, not a code fork. |
| D3 | **No shared resources between customers.** One BFF per customer env. Dedicated per-customer: OpenAI, AI Search, Doc Intelligence, Service Bus, Redis, Key Vault, App Insights. | Control plane is a separate Spaarke-internal service. Bicep deploys a full per-customer stack. |
| D4 | **Azure subscription per customer** = isolation + billing unit. `SpaarkeOwned` (default) or `CustomerOwned` (Lighthouse delegation). | Preflight + gate verify subscription access before infra steps. |
| D5 | **No bring-your-own-license.** Spaarke purchases user licenses. | Builds on r1 FR-11 per-env license resolution. |
| D6 | **Two identity presets**: `B2BGuest` (cross-tenant access) or `NativeAccount` (low-IT-friction). | Identity handler branches at user-creation only; gates differ (B2B needs consent verification). |
| D7 | **Consumption SKUs wherever possible.** Model versions pinned per ADR-020. | Bicep defaults favor consumption tiers; model versions are explicit pinned inputs. |
| D8 | **Three-layer architecture, built in order**: L1 handlers -> L2 control plane -> L3 front ends. | L1 + L2 are invariant; L3 is replaceable. Build sequence is L1 first. |
| D9 | **Claude Code is an authorized internal MCP client.** Never runtime, never customer-facing. | Operator skill (L3) calls L2 MCP tools; holds no provisioning logic. |
| D10 | **Gates verified, not inferred.** Orchestrator verifies gate state against Graph/ARM. ProvisioningRun is system of record. | Each gate = explicit verification handler writing result to run record. |
| D11 | **Every step idempotent and resumable.** Failed runs resume; they do not restart. | Maps onto ADR-004 (idempotent handlers, at-least-once, deterministic idempotency keys). |
| D12 | **Control plane = standalone service** (new App Service or Container App) in platform resource group. `platform.bicep` shrinks to control-plane-only resources. Per-customer AI resources (OpenAI, AI Search, Doc Intelligence) move to `customer.bicep`. | L2 is a separate service, not in the per-customer BFF. No shared AI resources between customers. |
| D13 | **ProvisioningRun state in Cosmos DB serverless.** Fleet UI is a future web app (not MDA dashboard in r1). | Sub-10ms writes, JSON-native state. Cosmos `spaarke-provisioning` database with `runs` container. Fleet web app deferred. |
| D14 | **Dataverse app user + Dataverse environment lifecycle = fully automated via Terraform Power Platform provider (v3, 2026-08-12).** Adopt the first-party Microsoft Terraform Power Platform provider for Dataverse env provisioning and application-user creation. Removes the semi-auto PPAC fallback. **Hybrid tooling** (see §4A): Bicep stays for Azure stamp, TF adds for Dataverse env lifecycle. Prerequisite: SP admin-bootstrapped via BAP API once per tenant; note SPs cannot create `Developer`-type envs (Sandbox/Production only). | H5 (env creation) + H10 (app user) run under TF plan; L2 orchestrator invokes `terraform apply` per phase. |
| D15 | **Hybrid environment profiles.** Named profiles (`spaarke-hosted`, `customer-owned`, `demo`, `trial`) set default parameter bundles; every parameter individually overridable. Preflight validates final parameter set. | Profiles are shorthand, not constraints. |
| D16 | **L3 orchestration via operator skill + Dataverse MCP for reads.** `/provision-environment` skill handles sequencing and gates (like `/deploy-new-release`). Existing Dataverse MCP tools handle data operations. No separate MCP server in r1. | Simplest viable L3; MCP server deferred to when MDA dashboard or Assistant needs arise. |
| D17 | **Decommission out of scope.** Existing `Decommission-Customer.ps1` remains operational as-is. Registry-aware teardown handlers deferred to r2. | No decommission handlers in this project. |
| D18 | **(v3, 2026-08-12) BFF doubles as consent-capture onboarding client.** For Model 2 customer-tenant deploys, the BFF exposes a consent callback endpoint that captures the customer admin's `tid` on consent grant and triggers the provisioning pipeline. Closes the "self-service" gap where admin consent is the one irreducible customer-tenant action. | New BFF endpoint + handler H0.5 (consent-capture) that seeds the run parameters. |
| D19 | **(v3, 2026-08-12) Per-tenant token-metering layer is a no-regret investment.** Build regardless of D3 outcome. Powers pricing (§3A) under any tenancy model — usage-passthrough for dedicated (D3), fair billing for shared trial tier. Implementation: APIM gateway with per-tenant token attribution OR app-level custom App-Insights metric keyed on `tenantId`. | New non-handler capability tracked in fast-follow list; not on the H0–H14 critical path but ships in r1. |

---

## 3A. ADR-Tensions — D3 Path A Amendment (added v3, 2026-08-12 per CLAUDE.md §6.5)

**Tension surfaced by 2026-08-12 assessment.** D3 (no shared resources between customers) is correct for regulated legal customers requiring physical isolation, and it dissolves cost-allocation (Azure Cost Management + tags = native per-customer bill = zero metering infra + honest usage-passthrough pricing). But three resources carry a **fixed monthly floor regardless of usage** — App Service Plan, Azure OpenAI (provisioned TPM), Azure AI Search (fixed tier) — that is brutal for trial/SMB prospects.

**Resolution path (Path A — project-scoped amendment, not a full ADR change).** Keep D3 dedicated as the **default** for real/regulated customers **and** add:

| Amendment | Rationale | Scope in r1 |
|---|---|---|
| **A1.** Shared, metered **trial/SMB tier** (Model 1 vertically-partitioned) with logical tenant isolation. | Fixed per-prospect floor is uneconomic; regulated-legal-grade isolation is over-engineered for trials. | **In r1**: control-plane supports "shared-tenant profile"; Bicep stack composition (`model1-shared.bicep`) treated as first-class alongside `model2-full.bicep`. |
| **A2.** Per-tenant **token-metering layer** (see D19) — no-regret. | Powers pricing under any tenancy choice; enforces per-tenant token budgets/quotas as runaway-loop guardrail; provides telemetry for eventual PTU decisions. | **In r1** as an engineering deliverable; not a handler, but ships. |
| **A3.** **Architectural cost controls** are documented as pipeline outputs to ensure cost-efficient defaults. | Prompt caching (~50–90% off cached input), model tiering, retrieval + context compression, per-tenant budgets, batch API, PAYG-first-then-PTU. | Documented in the deployment guide (Gap 4); no r1 code required — these are runtime BFF concerns tracked elsewhere. |

**Reference**: full economic analysis in [`PROJECT-UPDATE-2026-08-12.md`](PROJECT-UPDATE-2026-08-12.md) §4–5.

**Owner-signed exception**: this amendment is approved for the r1 scope. Path A is chosen over Path B (full ADR-013 amendment) because D3 as written remains correct for the dedicated default; the amendment adds a second first-class tier rather than reversing the default.

---

## 4. Three-Layer Architecture

### 4A. Tooling stack (added v3, 2026-08-12)

The provisioning pipeline is a **hybrid** stack. No single IaC/tool covers both Azure and Power Platform; the r1 design picks the right tool per layer rather than force one dialect across both.

| Layer | Tool | Why | Handlers using it |
|---|---|---|---|
| **Azure stamp** (per-customer resource stamp: RG, App Service Plan/App Service, KV, Storage, Service Bus, Redis, OpenAI, AI Search, Doc Intel, App Insights, **Cosmos DB**, optional SignalR) | **Bicep** (26 tuned modules + `platform.bicep` / `customer.bicep` / `model1-shared.bicep` / `model2-full.bicep`) | Deep Azure integration, existing production-hardened modules, matches ADR-020 model-pinning discipline. | H2a (infra) |
| **Dataverse environment lifecycle** (create/hydrate env, application-user registration, tenant-scoped SP admin) | **Terraform Power Platform provider** (Microsoft first-party) | The only IaC that covers Dataverse env lifecycle. Fully closes v2 D14 semi-auto gap. TF plan/state model gives us drift-detection. | H5 (env creation), H10 (app user) |
| **Managed solution import** (386 components across ~10 managed solutions) | **Package Deployer** (invoked from PS) + existing `Deploy-DataverseSolutions.ps1` | Package Deployer is the supported ALM tool for managed solution deploy with dependency ordering; existing script already handles ordering. | H6 |
| **AI Search indexes** (7 indexes, 3072-dim vectors) | Existing `infrastructure/ai-search/Deploy-AllIndexes.ps1` (PowerShell + Azure SDK) | Bicep can't author AI Search index schema; PS + SDK is the shortest path; script already exists. | H2b (indexes, sub-step of H2) |
| **Config-seed layer** (§9 of INVENTORY: type-lookups, actions, tools, playbooks, consumers, grid/field-mapping/workspace-layout configs, env-var values, AI model deployment records) | Existing PowerShell seeders (`Deploy-All-AI-SeedData.ps1`, `Seed-PlaybookConsumers.ps1`, `Deploy-SystemWorkspaceLayouts.ps1`, `Deploy-*ChartDefinitions.ps1`) invoked from a **declarative config-seed manifest** | Handles the drift between `scripts/seed-data/*.json` (2026-01 MVP) vs `infra/dataverse/**` (R7 current) via a single manifest that names the authoritative source per artifact. | H12a / H12b / H12c |
| **Web-resource / code-page deploy** | Existing `Deploy-Release.ps1` (hardened per Gap 2 — remove hardcoded `spaarkedev1`) | Existing pipeline; needs `customerId` parameter added. | H9 sub-step |
| **SPE container-type + container provisioning** | Existing `Create-NewContainerType.ps1`, `Register-*.ps1`, `New-BusinessUnitContainer.ps1` **switched to confidential-client (app-only) token** per SPE 403 fix | Delegated token now 403s ("public client not allowed"). Confidential-client + KV-stored cert is the current supported pattern. | H8 |
| **Consent-capture landing** (D18) | New BFF endpoint (Model 2 self-service onboarding) | Only irreducible customer-tenant admin action; capturing it in the BFF lets us trigger pipeline immediately on consent. | H0.5 |
| **L2 orchestration** | Custom .NET 8 control-plane service (see §4.2) that invokes the above per-handler | Small enough that we don't need Durable Functions / Temporal (rejected in §5.1); big enough that shell-script orchestration is too fragile. | All |
| **L3 operator UX** | `/provision-environment` Claude Code skill | v2 D16 unchanged. Skill calls L2 REST API (see §4.2 for the auth model). | — |

**Rejected alternatives**: (a) full-Terraform (no Azure module maturity match with our 26 Bicep modules; migration cost dwarfs benefit); (b) full-Bicep (no Power Platform provider); (c) Bicep + PS-only for Dataverse (v2 D14 semi-auto — inferior to TF Power Platform provider for env lifecycle).

### 4.1 Layer 1 — Deterministic Handlers

Provisioning steps implemented as idempotent handlers. Each handler is a self-contained, coarse-grained operation (deploy infrastructure, import solutions, deploy BFF) that fits the ADR-004 job contract individually.

**Existing substrate**: 13 production `IJobHandler` implementations prove the pattern at scale across RAG indexing, invoice processing, email analysis, attachment classification, and spend snapshots. Three-level idempotency is proven: Service Bus `MessageId` deduplication, Redis-backed `IdempotencyService` check/lock, Dataverse alternate keys/upserts.

**Handler catalog (v3, 2026-08-12)** — derived from `Provision-Customer.ps1` 13 steps + locked decisions + INVENTORY §9 config-seed layer + PROJECT-UPDATE §6 gap analysis. Splits several handlers to reflect reality (H2 → H2a/b/c; H12 → H12a/b/c) and adds H0.5 for Model 2 consent-capture (D18).

**Idempotency key `{schemaVer}` semantics (I3 resolved v3)**: version tokens are **deterministic content hashes / semantic versions of the artifact being deployed**, not run-attempt counters. `{bicepVer}` = git SHA of `infrastructure/bicep/`, `{solutionVer}` = solution version manifest hash, `{configVer}` = seed manifest hash, `{buildId}` = BFF CI build number. This makes re-running the same handler with unchanged inputs a no-op (three-level idempotency: Service Bus MessageId dedup + Redis `IdempotencyService` check/lock + Dataverse alternate-key upsert).

| # | Handler | Source logic | Gate | Idempotency key |
|---|---------|-------------|------|-----------------|
| H0 | Preflight / validate inputs | Step 1 + runbook checklist | — | `preflight-{customerId}-{paramHash}` |
| **H0.5 (v3)** | **Consent-capture callback** (Model 2 self-service only) | BFF `/api/onboarding/consent-callback` endpoint (D18) captures `tid` on admin consent, seeds run parameters | — | `consent-{customerId}-{tid}` |
| H1 | Subscription readiness | NEW (D4) — ARM verification | **Lighthouse delegation** (CustomerOwned) | `subready-{customerId}` |
| **H2a (was H2)** | Resource group + Azure infra (per-customer Bicep) — includes **Cosmos DB** (v3, BFF prereq), Redis, KV, App Service, OpenAI, AI Search, Doc Intel, App Insights, optional SignalR | Steps 2–3, `customer.bicep` + modules (or `model1-shared.bicep` per §3A A1) | — | `infra-{customerId}-{bicepVer}` |
| **H2b (v3, new)** | **AI Search index provisioning** (7 indexes; 3072-dim vectors) | Existing `infrastructure/ai-search/Deploy-AllIndexes.ps1` — invoked after H2a completes | — | `aisearch-{customerId}-{indexVer}` |
| H3 | Entra app registrations (per-customer, ~11 permission grants) | `Register-EntraAppRegistrations.ps1` (D2) — hardened to idempotent + tenant-aware (Model 1 vs Model 2) | **Admin consent granted** (Graph query) | `appreg-{customerId}-{tenantId}` |
| H4 | Key Vault secrets population + **`keyVaultReferenceIdentity` PATCH to UAMI** (silent-fail trap; see §4B) | Step 4 + PATCH; secrets stored as KV URI refs (B3) | — | `kv-{customerId}-{secretsVer}` |
| **H5 (v3, TF-driven)** | Dataverse environment creation | **TF Power Platform provider** (D14 v3) — `powerplatform_environment` resource; drift-detected | — | `dvenv-{customerId}` |
| H6 | Solution export/fix (managed) + Package Deployer import (~10 solutions, dependency-ordered) | Export (D1) + `Deploy-DataverseSolutions.ps1` + Package Deployer | — | `solimport-{customerId}-{solutionVer}` |
| H7 | 7 Dataverse env-var values + BFF app-settings (deploy-time token substitution) | Step 8 + `appsettings.template.json` token substitution + KV refs | — | `envvars-{customerId}-{configVer}` |
| **H8 (v3, confidential-client)** | SPE container type + root container | Existing scripts + **switch to confidential-client (app-only) token** — delegated token now 403s (`public client not allowed`). Cert bootstrapped from KV. | Container-type replication (up to 24h — **lead-time item, not in-pipeline wait**; see §9 north star) | `spe-{customerId}` |
| H9 | BFF deploy + app settings + **`Deploy-Release.ps1` Phase 4 hardened** (Gap 2 — `customerId`-driven, no `spaarkedev1` hardcode) | `Deploy-BffApi.ps1` + `auth-deployment-setup.md` + hardened Phase 4 | — | `bff-{customerId}-{buildId}` |
| **H10 (v3, TF-driven)** | Dataverse Application User (MI + BFF app-reg) + **MI Graph app-role parity** (silent-fail trap; see §4B) | **TF Power Platform provider** `powerplatform_user` resource; **replaces v2 semi-automated PPAC fallback**. Post-step: replicate ~11 Graph app-role grants onto MI service principal. | — | `appuser-{customerId}` |
| H11 | User provisioning (identity preset) | r1 registration flow (D6) | **B2B consent** (B2BGuest only) | `users-{customerId}` |
| **H12a (v3, PROMOTED from thin)** | **AI seed chain**: type-lookups → actions → tools → knowledge → skills → playbooks → output-types → **playbook consumers** (single AI routing surface, ADR-039) | Existing `scripts/seed-data/Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1`; **authoritative source per artifact declared in seed manifest** (resolves the `scripts/seed-data` MVP vs `infra/dataverse` R7 drift per INVENTORY §9) | — | `aiseed-{customerId}-{seedVer}` |
| **H12b (v3, PROMOTED)** | **App-config seed**: DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions | Existing per-module PS seeders + Web-API seeding recipes (per `FIELD-MAPPING-ADMIN-GUIDE.md`); declarative manifest | — | `configseed-{customerId}-{configSeedVer}` |
| **H12c (v3, PROMOTED)** | **Runtime references**: AI model deployment records (`sprk_aimodeldeployment`) point to this customer's Azure OpenAI deployment | Env-specific; runs after H2a (OpenAI deployed) + H12a (aitype lookups seeded) | — | `runtimerefs-{customerId}-{modelVer}` |
| H13 | End-to-end acceptance gate (Gap 4) | Extended `Validate-DeployedEnvironment.ps1` — asserts effects, not intentions (R7); checks BFF `/health`, sample analysis, sample document upload+index, workspace-layout render, wizard field-map | **Validation passed → registry `Ready`** | `validate-{customerId}-{buildId}` |
| **H14 (v3, enumerated)** | **Post-deploy integration wiring** (I1 resolved) — enumerated: (a) **two Exchange ApplicationAccessPolicies** (BFF app-reg + MI, both — silent-fail trap; §4B), (b) Graph webhook subscriptions per Communication/Email module (with HMAC signing keys from H4), (c) service endpoint webhooks (Dataverse → BFF), (d) S2S consent flows for the Dataverse S2S app registration | New scripting; each sub-step idempotent | — | `integrations-{customerId}-{integrationVer}` |

**Handler dependencies** (DAG, not just sequence):
```
H0 → H1 → H2a → { H2b, H4, H5 (TF) }
              ↓
              H4 → H3 (needs KV for secrets storage) → { H8, H9 }
                                                       ↓
H5 → H6 (solutions) → H7 → H10 (TF app-user, needs H6 solutions) → H11
                                    ↓
                            H12a → H12b → H12c → H14 → H13 (final gate)
```

**Model 2 self-service branch**: `H0.5 (consent-capture) → H0 → …` — the pipeline starts on consent-callback rather than operator-initiated.

### 4B. Silent-failure trap catalog (added v3, 2026-08-12)

Six known-issue guardrails baked into handlers as **verified post-conditions**, not runbook footnotes. Each trap has been diagnosed in production; ignoring any of them results in a BFF that boots but fails silently in a specific code path. Handlers assert the trap is cleared before reporting success.

| # | Trap | Where it bites | Handler that owns the fix | Verification |
|---|---|---|---|---|
| **T1** | **`keyVaultReferenceIdentity` not PATCHed to UAMI** — App Service resolves `@Microsoft.KeyVault(...)` refs with the wrong identity → all KV-ref settings become `null` at runtime | H4 completes but BFF startup fails resolving `Dataverse:ClientSecret` etc. | H4 | ARM read: App Service `keyVaultReferenceIdentity` == UAMI resource ID |
| **T2** | **MI not registered as Dataverse Application User** in the target env | Every BFF → Dataverse call 403s → surfaces as 500 to callers; Communication/Email module fails silently on subscription setup | H10 (was semi-auto in v2; TF-driven in v3) | Dataverse query: `systemusers?$filter=applicationid eq {mi-app-id}` returns 1 |
| **T3** | **MI Graph app-role parity broken** — the ~11 Graph app-roles granted on the BFF app-reg are NOT replicated onto the MI service principal | App-only Graph calls from BFF (SPE, mail, groups) 403 despite delegated flow working | H10 (post-step) | Graph query: MI SP `appRoleAssignments` includes all 11 role IDs |
| **T4** | **Only one Exchange ApplicationAccessPolicy** created (BFF app-reg, missing MI) — app-only mail calls scope-fail | Email/Communication module ingestion 403s despite delegated Mail.Send working | H14 (v3 enumerated) | Exchange `Get-ApplicationAccessPolicy` returns 2 entries (both principals) |
| **T5** | **Staging slot MI differs from prod slot MI** — KV RBAC granted only to prod slot → staging deploys can't resolve KV refs → cold-start failures on slot swap | Production deploy triggers a 503 window post-swap | H4 (extended) | ARM read of BOTH slots' MI object IDs; KV RBAC check for both |
| **T6** | **SPE container creation on delegated token 403s** (`public client not allowed`) | H8 fails on fresh customer with unhelpful auth error; blocks the whole pipeline | H8 (v3, confidential-client) | Container-type creation uses confidential-client cert from KV; no `az login`-style delegated flow |

**Additional latent traps tracked but not currently baked in** (fast-follow per PROJECT-UPDATE §6): two-source AI seed drift (H12a manifest resolves); hardcoded `spaarkedev1` in `Deploy-Release.ps1` Phase 4 (H9 hardened); Cosmos DB provisioning absence (H2a includes Cosmos).

### 4.2 Layer 2 — Control-Plane Service (v3 decisions locked)

The orchestration layer that sequences handlers, manages run state, enforces gates, and exposes APIs to front ends.

**Hosting (B2 resolved v3): App Service.** Parity with the BFF (same deploy tooling, same MI patterns, same App Insights integration, same slot-swap semantics). Container Apps was rejected — its scale-to-zero and rapid-scale strengths are not relevant to provisioning cadence (single-digit runs/day, minutes-per-handler), and the tooling divergence would double the ops surface. Placement: `rg-spaarke-platform-{env}`. `platform.bicep` is rebuilt to deploy only control-plane resources (App Service + App Service Plan, Cosmos DB, platform Key Vault, App Insights, Log Analytics). Per-customer AI resources (OpenAI, AI Search, Doc Intelligence) move to `customer.bicep` per D3.

**Protocol & auth (B1 resolved v3): REST API + AAD bearer token.** No MCP server in r1 (D16). The L2 API is a straight ASP.NET Core Minimal API secured by JWT bearer with:
- **Audience**: `api://spaarke-provisioning-controlplane-{env}` (dedicated app registration, one per env)
- **Issuer**: Spaarke Entra tenant (single-issuer; the control plane is Spaarke-internal, never customer-tenant)
- **Authorization**: RBAC via app-role assignment on the control-plane app-reg — `Operator` role required for all mutating endpoints; `Reader` role for `get_*` endpoints
- **OpenAPI**: exposed at `/swagger`; the L3 skill uses the generated schema to shape requests

**State store**: Cosmos DB serverless (`spaarke-provisioning` database, `runs` container), per D13. Sub-10ms writes, JSON-native for `completedPhases`, `gateStates`, `interStepState`.

**Concurrency (I5 resolved v3)**: Same-customer runs are serialized via optimistic concurrency on Dataverse `sprk_dataverseenvironment.sprk_currentrunid` (new field per §6.1). L2 attempts `sprk_currentrunid = null → newRunId` conditionally; conflict → 409 to caller with the winning run ID. Cross-customer runs execute in parallel (each is its own Cosmos partition + Dataverse row).

**Crash recovery (I6 resolved v3)**: On startup, L2 scans Cosmos for `status ∈ {Running, WaitingOnGate}` runs older than 2× median-handler-duration. For each orphaned run, L2 emits an `IJobHandler` job to resume from `currentPhase`. Handlers are idempotent (three-level: MessageId dedup + Redis idempotency lock + deterministic idempotency key per §4.1), so a duplicate-resume post-crash is safe.

**API surface** (REST endpoints, all AAD-protected):

| Method + Path | Auth role | Purpose |
|---|---|---|
| `POST /api/runs` | Operator | Initialize a run against an environment record (`create_provisioning_run`) |
| `POST /api/runs/{id}/preflight` | Operator | Execute H0, return parameter validation results |
| `GET /api/runs/{id}` | Reader | Return current phase, completed phases, gate states |
| `POST /api/runs/{id}/gates/{gateId}/advance` | Operator | Operator marks a manual gate as cleared (rare in v3; TF-driven H5/H10 eliminate most manual gates) |
| `POST /api/runs/{id}/resume` | Operator | Resume a failed run from the failure point (idempotency handles duplicate resumes) |
| `GET /api/runs/{id}/phases/{phaseId}/logs` | Reader | Return logs/output for a specific phase |
| `POST /api/runs/{id}/cancel` | Operator | Cancel an in-progress run |
| `POST /api/onboarding/consent-callback` | Anonymous (HMAC-verified) | **NEW v3 (D18)** — Model 2 self-service consent capture; validates `tid` on admin consent grant + kicks pipeline |

### 4.3 Layer 3 — Swappable Front Ends (D16)

| Front end | Timeline | Character |
|-----------|----------|-----------|
| Claude Code operator skill (`/provision-environment`) | This project | Interactive; uses existing Dataverse MCP tools for data ops + skill handles sequencing/gates (like `/deploy-new-release`) |
| Fleet web app | Future | Lightweight read-only UI over Cosmos `runs` container; not MDA dashboard in r1 |
| Spaarke Assistant integration | Future | Natural-language provisioning via the same L2 API |

---

## 5. ADR Constraint Analysis

### 5.1 ADR-004 — The Core Architectural Question

**The constraint**: All async work uses `IJobHandler` — one message, one handler, one outcome. "MUST NOT use Durable Functions."

**The friction**: ADR-004 was designed for single-shot, stateless operations. Provisioning is multi-phase, stateful, gate-dependent orchestration.

**Resolution**: ADR-004 applies at two different levels:

| Level | Fits ADR-004? | Rationale |
|-------|--------------|-----------|
| **Individual handlers** (H0-H14) | Yes | Each is a self-contained operation. Individually, they match the existing 13 production handlers. |
| **Run orchestration** (sequencing, gates, state) | No — and shouldn't | This is the L2 control plane's job. It's a NEW component with its own patterns, not governed by ADR-004. |

**Design approach (Option A)**: Handlers implement `IJobHandler`. The L2 control plane manages orchestration state and enqueues handlers. ADR-004 governs handler shape; the control plane builds a lightweight state machine (analogous to `Provision-Customer.ps1`'s state-file pattern, promoted to a proper run record).

**Options considered and rejected**:

| Option | Why rejected |
|--------|-------------|
| B: Extend ADR-004 with workflow-job variant | High blast radius on 13 existing handlers for a provisioning-specific need |
| C: Stay synchronous (current `DemoProvisioningService` pattern) | Blocks caller 30-60 min; no retry semantics; doesn't scale |
| D: Exempt provisioning entirely (Temporal/Durable Functions) | Adds infrastructure sprawl; the state-machine approach is proven and sufficient |

### 5.2 ADR-010 — DI Registration Pressure

BFF already at 269 registrations (17x the 15-line limit, acknowledged violation). Provisioning handlers register in the **control-plane service**, not the BFF — aligns with D3/D8/D12 and keeps BFF impact at zero.

### 5.3 ADR-017 — Status Granularity

Per-handler job status (ADR-017) governs individual handler outcomes. The **ProvisioningRun record** in Cosmos (D13) provides multi-phase orchestration state. No ADR change needed — different concerns, different stores.

### 5.4 What You'd Do Differently Without the ADRs

Only one thing: you might use Azure Durable Functions or Temporal for the control plane instead of building a custom state machine. Everything else (no plugins, Minimal API, ProblemDetails) correctly guides the design. The state-machine approach is more work but avoids infrastructure sprawl, and `Provision-Customer.ps1`'s state-file pattern already proves it works.

---

## 6. Data Model

### 6.1 `sprk_dataverseenvironment` — Fleet Inventory (extend existing)

The r1 entity has 16 columns deployed. Extend with provisioning infrastructure fields (v3 adds `sprk_currentrunid` per I5 concurrency, `sprk_tenancymodel` per §3A A1, `sprk_tenantid` per D18):

| Schema Name | Type | Purpose | v |
|---|---|---|---|
| `sprk_azuresubscriptionid` | Text(100) | Azure subscription hosting this environment | v2 |
| `sprk_resourcegroupname` | Text(200) | Resource group | v2 |
| `sprk_appservicename` | Text(200) | BFF App Service | v2 |
| `sprk_keyvaultname` | Text(200) | Key Vault | v2 |
| `sprk_containertypeid` | Text(100) | SPE container type | v2 |
| `sprk_provisionedon` | DateTime | When validation first passed | v2 |
| **`sprk_currentrunid`** | Text(40) | **v3 (I5)** — active ProvisioningRun ID; concurrency guard: L2 optimistically sets `null → newRunId`, conflict = 409. Cleared when the run reaches a terminal state. | v3 |
| **`sprk_tenancymodel`** | Choice | **v3 (§3A A1)** — `Model1Shared` (trial/SMB, shared platform floors) or `Model2Dedicated` (regulated, dedicated stamp per D3). Drives Bicep stack composition. | v3 |
| **`sprk_tenantid`** | Text(40) | **v3 (D18)** — Entra tenant ID. For Model 1: Spaarke tenant. For Model 2: customer tenant, captured via H0.5 consent-callback. | v3 |

### 6.2 ProvisioningRun — Cosmos DB Serverless (D13)

One execution of the pipeline against a target. Multiple runs per environment over time (initial provision, re-provision, repair).

**Database**: `spaarke-provisioning`
**Container**: `runs` (partition key: `/customerId`)

| Field | Type | Purpose |
|---|---|---|
| id | string (GUID) | Unique run identifier |
| customerId | string | Partition key + customer reference |
| environmentId | GUID | Lookup → `sprk_dataverseenvironment` |
| tenancyModel | string | v3 — `Model1Shared` or `Model2Dedicated` (mirrors `sprk_tenancymodel`) |
| status | string | NotStarted, Running, WaitingOnGate, Completed, Failed, Cancelled |
| currentPhase | string | Current handler ID (e.g., `H2a`, `H12b`) — string (not int) because v3 introduces sub-handlers |
| completedPhases | array | `[{ phase: "H2a", startedAt, completedAt, idempotencyKey, jobId }]` |
| **gateStates** (v3, I2) | object | `{ [gateId]: { status: "Pending"\|"Verified"\|"Cleared", verifiedAt, verifierHandler, evidence: {...} } }` — evidence is gate-specific (Graph query result for admin-consent, Dataverse query result for app-user, etc.) |
| parameters | object | Run parameters (**v3, B3**: secrets stored as KV URI refs — `{ "clientSecret": "@Microsoft.KeyVault(SecretUri=https://.../secrets/...)" }`, resolved at handler runtime via UAMI. No cleartext secrets in Cosmos.) |
| **interStepState** (v3, I2) | object | Enumerated keys: `bffAppRegId`, `s2sAppRegId`, `miObjectId`, `miClientId`, `containerTypeId`, `dataverseEnvUrl`, `openAiEndpoint`, `aiSearchEndpoint`, `cosmosEndpoint`, `systemUserId`, `speConsentCorrelationId`. Handlers write once; downstream handlers read. |
| profile | string | Environment profile used (`spaarke-hosted-model2`, `customer-owned-model2`, `spaarke-hosted-model1-trial`) |
| attemptCount | integer | Number of resume attempts (I6 crash recovery increments) |
| createdAt | datetime | Run creation |
| completedAt | datetime | Run completion (success or final failure) |
| errorDetail | string | Last error message |
| ttl | integer | Auto-expire after 365 days (Cosmos TTL) |

**Fleet visibility**: Future web app reads from Cosmos directly. No Dataverse sync needed in r1 — the `sprk_dataverseenvironment` entity provides fleet-level status via `Setup Status` field (already deployed) + `sprk_currentrunid` for in-flight runs.

---

## 7. Azure Resource Specification (Per-Customer)

Every Model 2 customer environment deploys a dedicated, isolated set of Azure resources per D3. Model 1 (trial/SMB per §3A A1) deploys the same resources except the three fixed-floor items (App Service Plan, OpenAI, AI Search) share the Spaarke platform tier.

> **v3 note**: [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) §7 is the **authoritative** BOM (with per-resource disposition, RBAC provisioning steps, and shared-vs-dedicated matrix). This section captures the *design* decisions — naming, catalog, and deployment order — that H2a's Bicep composition depends on. When the two disagree, INVENTORY wins.

**v3 additions to the v2 catalog**:
- **Cosmos DB (serverless)** — required by BFF runtime (AI sessions, prompts, audit, memory, feedback) per INVENTORY §7. v2 omitted this; BFF will not start without it. Partition by `/tenantId`. Per-customer in Model 2; shared with per-tenant partition in Model 1.
- **SignalR (optional / Null-Object)** — notifications spine realtime per ADR-034. Feature-gated; deploys only if `Notifications:SignalRSpine:Enabled=true`.
- **Two model stacks made first-class** — `model1-shared.bicep` (trial tier) alongside `model2-full.bicep` (dedicated). Not stack drift; deliberate composition per §3A A1.

### 7.1 Resource Naming Convention

| Resource Type | Pattern | Example (`customerId=acme`, `env=prod`) |
|---|---|---|
| Resource Group | `rg-spaarke-{customerId}-{env}` | `rg-spaarke-acme-prod` |
| Storage Account | `sprk{customerId}{env}sa` | `sprkacmeprodsa` |
| Key Vault | `sprk-{customerId}-{env}-kv` | `sprk-acme-prod-kv` |
| Service Bus | `spaarke-{customerId}-{env}-sbus` | `spaarke-acme-prod-sbus` |
| Redis Cache | `spaarke-{customerId}-{env}-cache` | `spaarke-acme-prod-cache` |
| App Service Plan | `sprk-{customerId}-{env}-plan` | `sprk-acme-prod-plan` |
| App Service (BFF) | `sprk-{customerId}-{env}-api` | `sprk-acme-prod-api` |
| OpenAI | `sprk-{customerId}-{env}-openai` | `sprk-acme-prod-openai` |
| AI Search | `sprk-{customerId}-{env}-search` | `sprk-acme-prod-search` |
| Document Intelligence | `sprk-{customerId}-{env}-docintel` | `sprk-acme-prod-docintel` |
| App Insights | `sprk-{customerId}-{env}-insights` | `sprk-acme-prod-insights` |
| Log Analytics | `sprk-{customerId}-{env}-logs` | `sprk-acme-prod-logs` |

### 7.2 Resource Catalog

| # | Resource | Bicep Module | Default SKU | Key Configuration |
|---|----------|-------------|-------------|-------------------|
| 1 | **Resource Group** | (subscription-level) | — | Tags: customer, environment, application, managedBy |
| 2 | **User-Assigned Managed Identity** | `uami.bicep` | — | Server-outbound identity (Graph app-only, Dataverse, Cosmos, KV). See §9.2 for RBAC + Graph roles. **INVENTORY §7 T1/T2/T3/T5 traps apply.** |
| 3 | **Key Vault** | `key-vault.bicep` | Standard | RBAC auth, soft delete 90d, purge protection, UAMI gets Secrets User role. **App Service `keyVaultReferenceIdentity` PATCHed to UAMI** (silent-fail T1). |
| 4 | **Storage Account** | `storage-account.bicep` | Standard_LRS | TLS 1.2, blob public access disabled, 3 containers (see 7.3) |
| 5 | **Service Bus** | `service-bus.bicep` | Standard | TLS 1.2, 4 queues + 1 membership topic (see 7.3), 5-min lock, 14-day TTL, DLQ enabled |
| 6 | **Redis Cache** | `redis.bicep` | Basic C0 (no VNet) / Premium P1 (VNet) | TLS-only (port 6380), allkeys-lru eviction |
| 7 | **Cosmos DB (serverless)** *(v3 added)* | `cosmos.bicep` | Serverless | AI sessions, prompts, audit, memory, feedback; partition `/tenantId`; UAMI granted **Cosmos DB Built-in Data Contributor**. **BFF will not start without this.** |
| 8 | **App Service Plan** | `app-service-plan.bicep` | S1 (Standard) | Linux |
| 9 | **App Service (BFF)** | `app-service.bicep` | — | .NET 8.0, HTTPS-only, always-on, HTTP/2, UAMI, health check `/health`. **Staging slot MI parity** (silent-fail T5). |
| 10 | **Azure OpenAI** | `openai.bicep` | S0 (`kind=AIServices`) | 4 model deployments (see 7.4). UAMI granted **Cognitive Services User** (wildcard; narrower OpenAI-User role insufficient for `kind=AIServices`). |
| 11 | **AI Search** | `ai-search.bicep` | Standard | Semantic search enabled, **7 indexes** (see Section 8; index creation is handler **H2b** via `Deploy-AllIndexes.ps1`, not Bicep) |
| 12 | **Document Intelligence** | `doc-intelligence.bicep` | S0 | prebuilt-layout model (see 7.5) |
| 13 | **App Insights + Log Analytics** | `monitoring.bicep` | PerGB2018 | 90-day retention, resource permissions enabled |
| 14 | **SignalR** (optional / Null-Object) *(v3 added)* | `signalr.bicep` | Free F1 / Standard S1 | Notifications spine realtime per ADR-034. Feature-gated (`Notifications:SignalRSpine:Enabled`). |
| 15 | **Content Safety** (optional) | `content-safety.bicep` | S0 | West US 2 or East US 2 only (Prompt Shields requirement) |
| 16 | **AI Foundry Hub + Project** (optional) | `ai-foundry-hub.bicep` | Basic | Prompt Flow orchestration, attached to storage + KV + insights |

**Shared-vs-dedicated disposition** (per §3A A1 + INVENTORY §11):

| Category | Resources | Model 1 (trial) | Model 2 (dedicated, D3) |
|---|---|---|---|
| 🔴 Always dedicated (cheap / customer-owned) | Dataverse, SPE, KV secrets, Storage, UAMI, Entra app config, Cosmos runtime data | dedicated | dedicated |
| 🟡 Fixed-floor levers (§3A A1 amendment) | App Service Plan, Azure OpenAI, AI Search | **shared** (metered per D19) | dedicated |
| 🟢 Safely shareable | Service Bus, App Insights/Log Analytics, Content Safety, Doc Intelligence, SignalR | shared | dedicated |

### 7.3 Sub-Resource Configuration

**Storage Account Containers:**

| Container | Purpose |
|-----------|---------|
| `temp-files` | Temporary document staging |
| `document-processing` | Processing intermediate files |
| `ai-chunks` | AI embedding chunks (lifecycle: tier to Cool after 30 days) |

**Service Bus Queues:**

| Queue | Purpose | Properties |
|-------|---------|------------|
| `sdap-jobs` | SDAP job processing | Lock 5min, DLQ on expiry, max delivery 10 |
| `document-indexing` | Document indexing tasks | Same |
| `ai-indexing` | AI indexing tasks | Same |
| `sdap-communication` | Communication/email processing | Same |

### 7.4 Azure OpenAI Model Deployments

| Deployment Name | Model | Version | Capacity (TPM) | Purpose |
|----------------|-------|---------|----------------|---------|
| `gpt-4o` | gpt-4o | 2024-08-06 | 150 | Primary analysis, complex reasoning |
| `gpt-4o-mini` | gpt-4o-mini | 2024-07-18 | 200 | High-volume analysis, playbook execution |
| `spaarke-gpt4o-mini` | gpt-4o-mini | 2024-07-18 | 30 | Isolated Layer 2 classification workloads |
| `text-embedding-3-large` | text-embedding-3-large | 1 | 350 | 3072-dim embeddings for all vector indexes |

Model version pinning per ADR-020. Embedding model change requires full AI Search re-index.

### 7.5 Document Intelligence Configuration

| Setting | Value | Notes |
|---------|-------|-------|
| Model | `prebuilt-layout` | Layout extraction (tables, paragraphs, sections) |
| SKU | S0 | Pay-per-page |
| Extraction routing | Feature-gated via `DocumentIntelligenceOptions.Enabled` | Routes between Document Intelligence and LlamaParse based on file type |

**File type routing (`DocumentParserRouter`):**

| Method | File Types | Engine |
|--------|-----------|--------|
| Native (direct read) | .txt, .md, .json, .csv, .xml, .html | No external service |
| Document Intelligence | .pdf, .docx, .doc | Azure Document Intelligence `prebuilt-layout` |
| Vision OCR | .png, .jpg, .jpeg, .gif, .tiff, .bmp, .webp | Multimodal LLM (gpt-4o) |
| Email | .eml, .msg | MimeKit + MsgReader (local) |

**Limits**: Max file size 10 MB, max input tokens 100K, max concurrent streams 3, timeout 30s, circuit breaker 3 failures / 60s break.

### 7.6 Deployment Order (v3 updated)

1. Resource Group
2. **UAMI** (created early so RBAC can be assigned against downstream resources by principalId)
3. Log Analytics + App Insights (monitoring, referenced by others)
4. Key Vault (secrets storage, created early so other modules can store outputs; UAMI → Secrets User)
5. Storage Account (UAMI → Blob Data Contributor)
6. Service Bus
7. Redis Cache
8. **Cosmos DB (serverless)** *(v3 added)* — BFF prereq; UAMI → Data Contributor
9. App Service Plan
10. OpenAI Service (`kind=AIServices`; UAMI → Cognitive Services User)
11. AI Search (**index creation is H2b, not part of this Bicep phase**)
12. Document Intelligence
13. **SignalR** (optional) *(v3 added)*
14. App Service (BFF, depends on plan + KV + all AI/data service endpoints; **`keyVaultReferenceIdentity` PATCHed to UAMI as post-deploy step per T1**)
15. Content Safety (optional)
16. AI Foundry Hub + Project (optional)

**Then, after Bicep completes** (post-H2a): H2b (7 AI Search indexes via `Deploy-AllIndexes.ps1`), H4 (KV secrets population + `keyVaultReferenceIdentity` PATCH per T1), then H3 onward.

### 7.7 Key Vault Secrets (Populated by H4)

**Infrastructure secrets (from Bicep outputs):**

| Secret Name | Source | Purpose |
|-------------|--------|---------|
| `redis-connection-string` | Redis deployment output | Cache access |
| `servicebus-connection-string` | Service Bus deployment output | Queue access |
| `storage-connection-string` | Storage deployment output | Blob access |
| `openai-api-key` | OpenAI deployment output | AI model access (fallback when MI auth unavailable per ADR-028 E-2) |
| `ai-openai-endpoint` | OpenAI deployment output | AI model endpoint |
| `aisearch-admin-key` | AI Search deployment output | Index management |
| `ai-search-endpoint` | AI Search deployment output | Search endpoint |
| `ai-docintel-endpoint` | Doc Intelligence deployment output | Document processing endpoint |
| `ai-docintel-key` | Doc Intelligence deployment output | Document processing access |
| `AppInsights-ConnectionString` | App Insights deployment output | Telemetry |

**Auth secrets (from H3 Entra app registration):**

| Secret Name | Source | Purpose |
|-------------|--------|---------|
| `BFF-API-ClientId` | App registration | BFF app registration client ID |
| `BFF-API-ClientSecret` | App registration credential | OBO flow client secret (24-month expiry) |
| `BFF-API-Audience` | `api://{bff-app-id}` | JWT audience validation |
| `Dataverse-S2S-ClientId` | S2S app registration | Dataverse service-to-service client ID |
| `Dataverse-S2S-ClientSecret` | S2S app registration credential | S2S authentication (24-month expiry) |
| `TenantId` | Customer Entra tenant | MSAL authority |

**Integration secrets:**

| Secret Name | Source | Purpose |
|-------------|--------|---------|
| `communication-webhook-signing-key` | Generated (48-byte base64) | HMAC-SHA256 for Graph subscription webhooks |
| `Email-WebhookSigningKey` | Generated (48-byte base64) | HMAC-SHA256 for Dataverse service endpoint webhooks |
| `customer-{customerId}-dataverse-url` | Dataverse environment | Cross-reference |
| `customer-{customerId}-spe-container-id` | SPE provisioning (H8) | Container reference |

### 7.8 Networking (Optional)

When VNet isolation is enabled (typically production):

| Component | CIDR | Purpose |
|-----------|------|---------|
| VNet | 10.0.0.0/16 | Customer network |
| snet-app | 10.0.1.0/24 | App Service delegation + KV service endpoint |
| snet-redis | 10.0.2.0/24 | Redis VNet injection (requires Premium SKU) |
| snet-pe | 10.0.3.0/24 | Private endpoints |

**Private DNS zones** (6): Key Vault, Storage Blob, Service Bus, OpenAI, AI Search, Document Intelligence.

**3 NSGs** with hardened rules per subnet.

---

## 8. AI Search Index Specification

> **v3 note (2026-08-12)**: [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) §9 references the current 7-index catalog and its deployment script (`infrastructure/ai-search/Deploy-AllIndexes.ps1`). This section captures the *design* — naming standard + field-level shape (audit-reference, may drift from JSON schemas; Phase A verifies). Handler **H2b** invokes `Deploy-AllIndexes.ps1` after H2a Bicep completes.

### 8.1 Index Naming Standard

**Convention**: `spaarke-{subject}-{qualifier}` where `{subject}` identifies the data domain and `{qualifier}` distinguishes index variants when needed.

### 8.2 Active Index Inventory (7 Indexes) — v3 corrected

All production indexes use **3072-dimensional vectors** with `text-embedding-3-large`, HNSW algorithm (m=4, efConstruction=400, efSearch=500, cosine metric), and semantic ranking. **v3 change**: was "8 indexes" — the `discovery-index` (1536-dim prototype) is deprecated/unused; current authoritative count is 7 per INVENTORY §9 & `Deploy-AllIndexes.ps1`.

| # | Index Name | Purpose | Module | Tenant Isolation | Schema Location |
|---|-----------|---------|--------|-----------------|-----------------|
| 1 | `spaarke-file-index` | Chunked document content from SPE files | RagService, FileIndexingService | `tenantId` filter | `infrastructure/ai-search/spaarke-file-index.json` |
| 2 | `spaarke-insights-index` | Observations and Precedents (discriminated by `artifactType`) | ObservationIndexUpserter, IndexRetrieveNode | `tenantId` filter | `infra/insights/schemas/spaarke-insights-index.index.json` |
| 3 | `spaarke-invoices-index` | Invoice chunks for financial analysis | InvoiceSearchService, InvoiceIndexingJobHandler | `tenantId` filter | `infrastructure/ai-search/invoice-index-schema.json` |
| 4 | `spaarke-playbook-embeddings` | Playbook descriptions for semantic discovery | PlaybookEmbeddingService | N/A (global) | `infrastructure/ai-search/playbook-embeddings.json` |
| 5 | `spaarke-knowledge-index` | Knowledge base documents | RagService (legacy path) | `tenantId` + `privilege_group_ids` | `infrastructure/ai-search/spaarke-knowledge-index-v2.json` |
| 6 | `spaarke-rag-references` | Golden reference knowledge (curated enterprise knowledge) | ReferenceRetrievalService | `tenantId` filter | `infrastructure/ai-search/spaarke-rag-references.json` |
| 7 | `spaarke-records-index` | Dataverse entity records (Matter, Project, Invoice, etc.) | RecordSearchService, DataverseIndexSyncService | Dataverse-layer (no `tenantId` field) | `infrastructure/ai-search/spaarke-records-index.json` |
| 8 | `spaarke-session-files` | Session-scoped chat uploads (per ADR-014) | RagService (session mode), SessionFilesCleanupJob | `tenantId` + `sessionId` dual filter | `infrastructure/ai-search/spaarke-session-files.json` |

**Deprecated / removed from active catalog**: `spaarke-knowledge-index-v2` (dual-vector 1536+3072) — replaced by `spaarke-knowledge-index` with 3072-only vectors. `discovery-index` (1536-dim prototype) — dropped in v3 count.

### 8.3 Index Field Specifications

**v3 note**: field-level schemas live in the JSON files under `infrastructure/ai-search/` — those are the source of truth deployed by H2b. The tables below are the v2 audit-reference snapshot; a Phase A field-by-field diff against the current JSON schemas is a Phase A verification item (per INVENTORY §12).

#### spaarke-file-index

| Field | Type | Searchable | Filterable | Vector |
|-------|------|-----------|-----------|--------|
| id | string (key) | — | — | — |
| tenantId | string | — | Yes | — |
| deploymentId | string | — | Yes | — |
| containerId | string | — | Yes | — |
| speFileId | string | — | Yes | — |
| documentId | string | — | Yes | — |
| content | string | Yes | — | — |
| contentVector3072 | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| documentVector3072 | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| fileName, documentName | string | Yes | Yes | — |
| documentType, fileType | string | — | Yes | — |
| chunkIndex, chunkCount | int | — | Yes | — |
| parentEntityType, parentEntityId | string | — | Yes | — |
| privilege_group_ids | Collection(string) | — | Yes | — |
| tags, metadata | string | Yes | — | — |
| createdAt, updatedAt | DateTimeOffset | — | Yes | — |

#### spaarke-insights-index

| Field | Type | Searchable | Filterable | Vector |
|-------|------|-----------|-----------|--------|
| id | string (key) | — | — | — |
| tenantId | string | — | Yes | — |
| artifactType | string | — | Yes | — |
| subject, predicate | string | Yes | Yes | — |
| content | string | Yes | — | — |
| contentVector | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| value | Complex (raw, displayHint) | — | — | — |
| evidence | Collection(Complex: refType, ref, quote) | — | — | — |
| scope | Complex (matterId, entityType, entityId, tenantId, practiceArea) | — | Yes (sub-fields) | — |
| confidence | double | — | Yes | — |
| status | string | — | Yes | — |
| asOf | DateTimeOffset | — | Yes | — |
| producedBy | string | — | Yes | — |

#### spaarke-invoices-index

| Field | Type | Searchable | Filterable | Vector |
|-------|------|-----------|-----------|--------|
| id | string (key) | — | — | — |
| tenantId | string | — | Yes | — |
| invoiceId, documentId | string | — | Yes | — |
| matterId, projectId, vendorOrgId | string | — | Yes | — |
| vendorName, invoiceNumber | string | Yes | Yes | — |
| content | string | Yes | — | — |
| contentVector | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| invoiceDate | DateTimeOffset | — | Yes | — |
| totalAmount | double | — | Yes | — |
| currency | string | — | Yes | — |
| chunkIndex | int | — | Yes | — |
| documentType | string | — | Yes | — |
| indexedAt | DateTimeOffset | — | Yes | — |

#### spaarke-playbook-embeddings

| Field | Type | Searchable | Filterable | Vector |
|-------|------|-----------|-----------|--------|
| id | string (key) | — | — | — |
| playbookId | string | — | Yes | — |
| playbookName | string | Yes | Yes | — |
| description | string | Yes | — | — |
| contentVector3072 | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| triggerPhrases | Collection(string) | Yes | — | — |
| recordType, entityType | string | — | Yes | — |
| tags | Collection(string) | Yes | Yes | — |

#### spaarke-knowledge-index

| Field | Type | Searchable | Filterable | Vector |
|-------|------|-----------|-----------|--------|
| id | string (key) | — | — | — |
| tenantId, deploymentId, deploymentModel | string | — | Yes | — |
| knowledgeSourceId, knowledgeSourceName | string | Yes | Yes | — |
| documentId, speFileId | string | — | Yes | — |
| content | string | Yes | — | — |
| contentVector3072 | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| documentVector3072 | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| fileName, documentName | string | Yes | Yes | — |
| parentEntityType, parentEntityId | string | — | Yes | — |
| privilege_group_ids | Collection(string) | — | Yes | — |
| tags, metadata | string | Yes | — | — |
| chunkIndex, chunkCount | int | — | Yes | — |
| createdAt, updatedAt | DateTimeOffset | — | Yes | — |

#### spaarke-rag-references

| Field | Type | Searchable | Filterable | Vector |
|-------|------|-----------|-----------|--------|
| id | string (key) | — | — | — |
| tenantId | string | — | Yes | — |
| knowledgeSourceId, knowledgeSourceName | string | Yes | Yes | — |
| domain | string | — | Yes | — |
| content | string | Yes | — | — |
| contentVector3072 | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| tags | Collection(string) | Yes | Yes | — |
| version | string | — | Yes | — |
| chunkIndex, chunkCount | int | — | Yes | — |
| createdAt, updatedAt | DateTimeOffset | — | Yes | — |

#### spaarke-records-index

| Field | Type | Searchable | Filterable | Vector |
|-------|------|-----------|-----------|--------|
| id | string (key) | — | — | — |
| recordType | string | — | Yes | — |
| recordName, recordDescription | string | Yes | — | — |
| organizations, people, referenceNumbers | Collection(string) | Yes | Yes | — |
| keywords | Collection(string) | Yes | Yes | — |
| contentVector | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| dataverseRecordId, dataverseEntityName | string | — | Yes | — |
| privilege_group_ids | Collection(string) | — | Yes | — |
| lastModified | DateTimeOffset | — | Yes | — |

#### spaarke-session-files

| Field | Type | Searchable | Filterable | Vector |
|-------|------|-----------|-----------|--------|
| id | string (key) | — | — | — |
| tenantId | string | — | Yes | — |
| sessionId | string | — | Yes | — |
| documentId, speFileId | string | — | Yes | — |
| content | string | Yes | — | — |
| contentVector3072 | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| documentVector3072 | Collection(Edm.Single) | — | — | 3072-dim HNSW |
| documentName, fileName | string | Yes | Yes | — |
| documentType, fileType | string | — | Yes | — |
| chunkIndex, chunkCount | int | — | Yes | — |
| tags, metadata | string | Yes | — | — |
| createdAt, updatedAt | DateTimeOffset | — | Yes | — |

### 8.4 Index Configuration (AiSearchOptions)

BFF configuration maps (`src/server/api/Sprk.Bff.Api/Configuration/AiSearchOptions.cs`):

| Config Key | Index Name | Notes |
|-----------|-----------|-------|
| `AiSearch:FilesIndexName` | `spaarke-file-index` | Primary document search |
| `AiSearch:InsightsIndexName` | `spaarke-insights-index` | Observations + Precedents |
| `AiSearch:RagReferencesIndexName` | `spaarke-rag-references` | Golden references |
| `AiSearch:SessionFilesIndexName` | `spaarke-session-files` | Session-scoped uploads |
| `AiSearch:KnowledgeIndexName` | `spaarke-knowledge-index` | Knowledge base |
| `AiSearch:DiscoveryIndexName` | (deprecated) | Legacy discovery |
| `AiSearch:AllowedIndexes` | Operator-configured allow-list | Per-environment index access |

Invoice and playbook indexes are configured via their respective options classes (`FinanceOptions`, `PlaybookEmbeddingService` factory).

### 8.5 Index Provisioning — **Handler H2b (v3, standalone handler)**

After H2a Bicep completes, handler **H2b** invokes `infrastructure/ai-search/Deploy-AllIndexes.ps1` which applies each index JSON schema definition via the Azure Search REST API. The 7 JSON schema files in `infrastructure/ai-search/` are the source of truth. Idempotency key `aisearch-{customerId}-{indexVer}` where `{indexVer}` = git SHA of `infrastructure/ai-search/`.

**Action item for Phase A** (per INVENTORY §12 verification backlog): Audit the 7 JSON schema files against current BFF service field usage. Confirm field-level alignment. Standardize any naming inconsistencies (e.g., the dev-only `spaarke-invoices-dev` suffix per §18 item 5).

---

## 9. Identity & Access Specification

### 9.1 Entra App Registrations (2 Per Customer)

**v3 tenancy note (per PROJECT-UPDATE §3)**: Do **not** create a per-customer Entra tenant. Use **one Spaarke tenant + one multitenant BFF app** for Model 1 (shared trial) and, for Model 2 customer-owned tenants, register the same multitenant BFF app in the customer tenant (per D18 consent-capture). The app registrations below live in whichever tenant hosts the deployment; the sign-in audience is `AzureADMultipleOrgs` for Model 2 to enable customer-tenant self-service (v3 change from v2's single-tenant `AzureADMyOrg`).

#### BFF API App Registration

| Property | Value |
|----------|-------|
| Display Name | `spaarke-bff-api-{customerId}-{env}` |
| Sign-in Audience | **`AzureADMultipleOrgs`** *(v3 changed)* — enables Model 2 customer-tenant consent (D18) |
| Platform | Web |
| App ID URI | `api://{bff-app-id}` |
| Client Secret Expiry | 24 months — **stored as KV reference, resolved via UAMI at runtime (B3)** |
| Redirect URI | `https://{api-domain}/.auth/login/aad/callback` + `https://{api-domain}/api/onboarding/consent-callback` *(v3, D18)* |
| Exposed Scope | `api://{bff-app-id}/user_impersonation` |
| Known Client Applications | PCF client app ID, Code Page client app ID (set post-creation) |

**API Permissions (5):**

| API | Permission | Type | GUID |
|-----|-----------|------|------|
| Microsoft Graph | Files.ReadWrite.All | Delegated | `75359482-378d-4052-8f01-80520e7db3cd` |
| Microsoft Graph | Sites.ReadWrite.All | Delegated | `89fe6a52-be36-487e-b7d8-d061c450a026` |
| Microsoft Graph | User.Read | Delegated | `e1fe6dd8-ba31-4d61-89e7-88639da4683d` |
| Microsoft Graph | Mail.Send | Delegated | `e383f46e-2787-4529-855e-0e479a3ffac0` |
| Dynamics CRM | user_impersonation | Delegated | `78ce3f0f-a1ce-49c2-8cde-64b5c0896db4` |

#### Dataverse S2S App Registration

| Property | Value |
|----------|-------|
| Display Name | `spaarke-dataverse-s2s-{customerId}-{env}` |
| Sign-in Audience | AzureADMyOrg |
| Platform | Service-to-service (no redirect URIs) |
| Client Secret Expiry | 24 months |

**API Permissions (1):**

| API | Permission | Type | GUID |
|-----|-----------|------|------|
| Dynamics CRM | user_impersonation | Delegated | `78ce3f0f-a1ce-49c2-8cde-64b5c0896db4` |

### 9.2 Managed Identity

**Type (v3 corrected)**: **User-Assigned Managed Identity (UAMI)** provisioned by Bicep `uami.bicep`, then bound to App Service (both prod + staging slots per T5). v2 said "system-assigned" — INVENTORY §7 confirms current pattern is UAMI (needed for slot-swap parity and cross-resource RBAC assignment before App Service exists).

**Environment variable bindings (5):**

| Variable | Purpose |
|----------|---------|
| `Graph__ManagedIdentity__ClientId` | Graph options validator |
| `ManagedIdentity__ClientId` | Generic MI options |
| `AZURE_CLIENT_ID` | DefaultAzureCredential |
| `UAMI_CLIENT_ID` | Custom BFF usage |
| MI principal ID | Dataverse Application User registration + Graph role assignments |

**Azure RBAC Roles:**

| Role | Scope | GUID |
|------|-------|------|
| Key Vault Secrets User | Customer Key Vault | `4633458b-17de-408a-b874-0445c86b69e6` |
| Storage Blob Data Contributor | Customer Storage Account | (standard) |
| Cosmos DB Built-in Data Contributor | Cosmos Account (if used) | `00000000-0000-0000-0000-000000000002` |
| Cognitive Services User | OpenAI Service | `a97b65f3-24c7-4388-baec-2e87135dc908` |

**Note (ADR-028 E-2)**: MI auth for OpenAI on `kind=AIServices` accounts has known reliability issues. Fallback: `AzureOpenAI__ApiKey` Key Vault reference. H4 populates both MI role assignment and API key secret.

**Graph App Roles (on UAMI service principal, granted via Graph API — ~11 total per INVENTORY §7 T3):**

| Permission | Type | Purpose |
|------------|------|---------|
| FileStorageContainer.Selected | App role | SPE container access |
| Files.ReadWrite.All | App role | SPE file operations |
| Sites.ReadWrite.All | App role | SPE site operations |
| User.Read.All | App role | User lookups |
| Group.Read.All | App role | Group membership checks |
| Mail.Send | App role | App-only mail sending |
| Mail.Read | App role | Email module ingestion |
| MailboxSettings.Read | App role | Mailbox settings |
| ChannelMessage.Read.All | App role | Teams/channel message ingestion (per Communication module) |
| Chat.Read.All | App role | Teams chat ingestion |
| Presence.Read.All | App role | User presence lookups |

**Silent-fail trap T3 (§4B)**: parity between BFF app-reg permission grants and UAMI SP app-role assignments MUST be verified. Delegated flow can pass while app-only 403s if UAMI is missing a role. H10 post-step queries Graph `servicePrincipals/{uamiObjectId}/appRoleAssignments` to assert count matches expected list.

### 9.3 Dataverse Security

**Application Users (2, created in H10 via TF Power Platform provider — v3, D14):**

| Principal | Security Role | Business Unit | Method |
|-----------|--------------|---------------|--------|
| BFF app registration (by app ID) | System Administrator | Root | **TF `powerplatform_user` resource** — fully automated (v3, replaces v2 semi-auto PPAC fallback) |
| UAMI service principal (by UAMI app ID) | System Administrator | Root | **TF `powerplatform_user` resource** — fully automated |

**Silent-fail trap T2 (§4B)**: MI-not-registered-as-Dataverse-App-User causes every BFF→Dataverse call to 403→500 silently. H10 post-step queries `systemusers?$filter=applicationid eq {uami-app-id}` and asserts returned count = 1.

**Custom Security Roles (shipped in SpaarkeCore solution):**

| Role | Audience | Permissions |
|------|----------|-------------|
| Spaarke User | All end users | Read/create/write to Spaarke entities |
| Spaarke AI Analysis User | Users running analyses | Read documents, create analyses, view results |
| Spaarke AI Analysis Admin | Administrators | All user + manage playbooks, configure AI settings |

Roles are defined in the managed solution and imported by H6. User assignment is post-provisioning (customer admin task).

### 9.4 Exchange Online Application Access Policies (Handler H14 sub-step)

Required if Communication/Email modules are enabled. **Silent-fail trap T4 (§4B)**: creating only one policy (BFF app-reg, omitting UAMI) causes app-only mail to 403 while delegated Mail.Send works — hidden until the Email/Communication module runs.

**Mail-enabled security group**: `Spaarke Email Access` (`spaarke-central-email@{customer-tenant}`)

**Two ApplicationAccessPolicy objects (both mandatory):**
1. BFF app registration → `RestrictAccess` scoped to group
2. UAMI service principal → `RestrictAccess` scoped to group

**Propagation**: Up to 30 minutes before Graph mailbox calls succeed — **lead-time item, not in-pipeline wait** (per §9 north star framing).

H14 verification: `Get-ApplicationAccessPolicy` returns 2 entries and both `AppId`s match expected principals.

### 9.5 Webhook Security

| Webhook | Signing Key Secret | Algorithm | Header | Endpoint |
|---------|-------------------|-----------|--------|----------|
| Communication (Graph subscriptions) | `communication-webhook-signing-key` | HMAC-SHA256 | `X-MSHUB-Signature` | `/api/communications/incoming-webhook` |
| Email (Dataverse service endpoint) | `Email-WebhookSigningKey` | HMAC-SHA256 | `Authorization-Context` | `/api/v1/emails/webhook-trigger` |

Both secrets are 48-byte base64, generated during H4, fail-closed if missing.

---

## 10. Parameter Model & Customer Configuration

### 10.1 Environment Profiles (D15 + §3A A1 v3 expansion)

Named profiles set default parameter bundles. Every parameter is individually overridable. Preflight (H0) validates the final parameter set. **v3 (§3A A1)**: profiles now bind to a `tenancymodel` — `Model1Shared` for the trial/SMB tier, `Model2Dedicated` for the default dedicated stamp.

| Profile | Tenancy Model | Bicep Stack | Identity Preset | Subscription Target | Default SKUs | Notable Gates |
|---------|--------------|------------|----------------|-------------------|-------------|---------------|
| `spaarke-hosted-model2` *(was `spaarke-hosted`)* | Model2Dedicated | model2-full | B2BGuest | SpaarkeOwned | S1/Standard | Lighthouse: skip |
| `customer-owned-model2` *(was `customer-owned`)* | Model2Dedicated | model2-full | NativeAccount | CustomerOwned | Customer-specified | Lighthouse: required |
| **`spaarke-hosted-model1-trial`** *(v3, new — §3A A1)* | Model1Shared | model1-shared | B2BGuest | SpaarkeOwned | Free/Basic on fixed-floor resources (App Service Plan, OpenAI, AI Search shared with platform); dedicated on everything else | Per-tenant token budget (D19) enforced |
| `demo` | model2-full (reduced) | B2BGuest | SpaarkeOwned | B1/Basic/Free | Lightweight validation |
| `trial` | model2-full (time-limited) | B2BGuest | SpaarkeOwned | B1/Basic | Expiry date gate |

### 10.2 Run Parameters (v3 — secrets moved to KV refs per B3)

**Required (7):**

| Parameter | Type | Constraints | Purpose |
|-----------|------|------------|---------|
| `customerId` | string | 3-10 chars, lowercase alphanumeric | Resource naming, partition key |
| `displayName` | string | Human-readable | Dataverse environment display name |
| `tenantId` | GUID | Valid Entra tenant | Auth authority, env vars. Model 2: captured via H0.5 consent-callback (D18); Model 1: Spaarke tenant. |
| `clientId` | GUID | Service principal | PAC CLI and Admin API auth |
| **`clientSecretKvRef`** *(v3, was `clientSecret`)* | KV URI | `@Microsoft.KeyVault(SecretUri=...)` OR `certificateThumbprint` | **B3**: secret stored in **platform KV** (never in Cosmos parameters), resolved at handler runtime via UAMI. Cleartext secrets forbidden in Cosmos. |
| `bffApiAppId` | GUID | BFF app registration | Env var `sprk_BffApiAppId` |
| `msalClientId` | GUID | Typically same as bffApiAppId | Env var `sprk_MsalClientId` |

**Optional (with defaults):**

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `profile` | `spaarke-hosted-model2` *(v3, was `spaarke-hosted`)* | Environment profile (sets other defaults; see §10.1) |
| `tenancyModel` | derived from profile | `Model1Shared` or `Model2Dedicated` (v3, §3A A1) |
| `bffApiBaseUrl` | `https://api.spaarke.com` | Env var `sprk_BffApiBaseUrl` |
| `azureOpenAiEndpoint` | (empty; derived from H2a Bicep output) | Env var `sprk_AzureOpenAiEndpoint` |
| `shareLinkBaseUrl` | (empty) | Env var `sprk_ShareLinkBaseUrl` |
| `environmentName` | `prod` | Resource naming suffix |
| `location` | `westus2` | Azure region |
| `dataverseRegion` | `unitedstates` | Power Platform region |
| `platformKeyVaultName` | `sprk-platform-prod-kv` | Control-plane KV (secrets storage; parameters reference URIs here) |
| `platformResourceGroup` | `rg-spaarke-platform-prod` | Control-plane RG |
| `resumeFromPhase` *(v3, was `resumeFromStep`)* | (auto-detect from Cosmos) | Resume from specific handler ID (e.g., `H12b`) |
| `skipDataverse` | false | Skip H5–H8 (when Dataverse env already exists) |
| **`tokenBudgetMonthlyUSD`** *(v3, per D19)* | profile-dependent (Model 1 trial: capped; Model 2: unlimited) | Per-tenant token budget enforced by metering layer |

### 10.3 Dataverse Environment Variables (7 per-customer values — v3 reconciled to INVENTORY §9)

**v3 count reconciliation**: [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) §9 (authoritative) lists **7 per-customer env-var values** (21 total `sprk_*` env-var **definitions** in the solution — not all are per-customer set-at-provisioning). v2's list of 8 included `sprk_DefaultPlaybookId` and `sprk_ApplicationInsightsKey` and omitted `sprk_ShareLinkBaseUrl`. The reconciled list matches what `Provision-Customer.ps1` step 8 actually sets today plus the H7 addition.

Set by H7. Queried at runtime by PCF controls and Code Pages via `environmentvariabledefinition` + `environmentvariablevalue` entities. 5-minute in-memory cache + 60-minute localStorage persistence. Client fails at startup with clear config error if any is missing (no hardcoded URL fallbacks).

**No migration to Azure App Configuration has occurred** — Dataverse environment variables remain the canonical client-side configuration mechanism. The `environmentVariables.ts` utility (`src/client/pcf/shared/utils/`) is the single retrieval point.

| # | Schema Name | Display Name | Purpose | Source |
|---|---|---|---|---|
| 1 | `sprk_BffApiBaseUrl` | BFF API Base URL | Backend API endpoint (normalized: no trailing slash, no `/api` suffix) | Parameter `bffApiBaseUrl` |
| 2 | `sprk_BffApiAppId` | BFF API App ID | OAuth scope audience | Parameter `bffApiAppId` |
| 3 | `sprk_MsalClientId` | MSAL Client ID | MSAL public client ID for Dataverse-hosted SPAs | Parameter `msalClientId` |
| 4 | `sprk_TenantId` | Tenant ID | Entra tenant (MSAL authority) | Parameter `tenantId` |
| 5 | `sprk_AzureOpenAiEndpoint` | Azure OpenAI Endpoint | AI features endpoint | H2a Bicep output |
| 6 | `sprk_ShareLinkBaseUrl` | Share Link Base URL | External share-link generation | Parameter `shareLinkBaseUrl` |
| 7 | `sprk_SharePointEmbeddedContainerId` | SPE Container ID | Document storage container | H8 output |

**Additional env-var definitions in solution** (21 total per INVENTORY §3) — these are set once from seed data OR read by BFF (not per-customer at provisioning): `sprk_DefaultPlaybookId` (seeded by H12a), `sprk_ApplicationInsightsKey` (BFF-side, not client-side), and ~11 module-specific config keys.

### 10.4 BFF App Settings (26 Configuration Sections)

The BFF uses 26 `IOptions<T>` configuration classes registered via `ConfigurationModule.cs`. All settings are sourced from `appsettings.json` + Key Vault references + deploy-time token substitution (`#{TOKEN}#` format).

**Key configuration sections (customer-specific values in bold):**

| Section | Key Properties | Secret? |
|---------|---------------|---------|
| `AzureAd` | **TenantId**, **ClientId**, **Audience** | No (except ClientSecret via KV ref) |
| `ConnectionStrings` | **ServiceBus**, **Redis** | Yes (KV refs) |
| `Dataverse` | **ServiceUrl**, ClientSecret | Yes (KV refs) |
| `AzureOpenAI` | **Endpoint**, ApiKey, ChatModelName, EmbeddingModelName, ClassificationModelName | Yes (KV refs) |
| `AiSearch` | **Endpoint**, ApiKeySecretName, index names, AllowedIndexes | Partial |
| `DocumentIntelligence` | **DocIntelEndpoint**, DocIntelKey, models, limits, file type routing | Yes (KV refs) |
| `Graph` | ManagedIdentity.Enabled, **ManagedIdentity.ClientId** | No |
| `Redis` | Enabled, **ConnectionString**, InstanceName, expiration settings | Yes |
| `ServiceBus` | **ConnectionString**, QueueName, MaxConcurrentCalls | Yes |
| `ApplicationInsights` | **ConnectionString** | Yes (KV ref) |
| `Communication` | DefaultMailbox, ArchiveContainerId, webhook URLs/keys, ApprovedSenders | Yes (partial) |
| `Email` | Enabled, DefaultContainerId, processing flags, webhook keys | Yes (partial) |
| `DemoProvisioning` | DefaultEnvironment, AccountDomain, DemoUsersGroupId, Licenses, Environments | No |
| `CosmosPersistence` | **Endpoint**, DatabaseName | No |
| `Analysis` | Enabled, MultiDocumentEnabled, model names, search config, streaming | Partial |
| `Spaarke` | Graph.TodoSync.Enabled, Environment.OrgUrl, DefaultAppId | No |
| `Cors` | **AllowedOrigins** (Dataverse + Teams origins) | No |
| `PowerPages` | **BaseUrl** | No |
| `AgentToken` | TenantId, ClientId, ClientSecret, agent config | Yes |
| `CopilotAgent` | Feature capability gates (5 booleans) | No |
| `Insights` | Playbook name → GUID mapping | No |
| `BingSearch` | ApiKey, Endpoint, MaxResults | Yes |
| `LlamaParse` | ApiKey, BaseUrl, timeout, max pages, enabled | Partial |
| `Indexing` | PostUploadEnqueueEnabled, MaxIndexableBytes | No |
| `ScheduledRagIndexing` | Enabled, interval, limits, TenantId | No |
| `GraphResilience` | Retry, circuit breaker, timeout settings | No |

**Deploy-time tokens** (substituted by CI/CD): `#{TENANT_ID}#`, `#{API_APP_ID}#`, `#{DEFAULT_CT_ID}#`, `#{KEY_VAULT_URL}#`, `#{DATAVERSE_ORG_NAME}#`, `#{REDIS_INSTANCE_NAME}#`, `#{SERVICE_BUS_QUEUE_NAME}#`, `#{AI_SUMMARIZE_MODEL}#`, `#{AI_EMBEDDING_MODEL}#`, `#{AI_CHAT_MODEL_NAME}#`, `#{AI_SEARCH_INDEX_NAME}#`, `#{SHARED_KNOWLEDGE_INDEX_NAME}#`, `#{DEPLOYMENT_ENVIRONMENT}#`, `#{CUSTOMER_TENANT_ID}#`, `#{RECORD_MATCHING_ENABLED}#`, `#{ANALYSIS_ENABLED}#`, `#{MULTI_DOCUMENT_ENABLED}#`, `#{COPILOT_SSO_PROVIDER_APP_ID}#`, `#{COPILOT_AGENT_APP_ID}#`.

H7 (environment variables) sets Dataverse env vars. H9 (BFF deploy) applies `appsettings.template.json` with token substitution + Key Vault references.

### 10.5 Output Artifact

`environment-config-{customerId}-{env}.json` — canonical config reference generated at H12 (seeding step). Single source of truth for all customer configuration values post-provisioning. Contains customer metadata, Dataverse URL + env vars, Azure resource names + endpoints.

---

## 11. Existing Asset Disposition

Verified against the codebase 2026-06-15 (v2) + refreshed 2026-08-12 (v3) — full authoritative BOM in [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md). Legend: **PORT** = logic feeds a handler; **REUSE** = consumed as-is; **REBUILD** = concept kept, form changes; **NEW** = does not exist.

**v3 delta**: INVENTORY §11 makes shared-vs-dedicated disposition explicit; INVENTORY §4 (33 PCF folders / 7 in-use) and §9 (two-source AI seed drift) surface packaging-gap risks the v2 disposition tables missed.

### 11.1 Scripts and Orchestration

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `Provision-Customer.ps1` | `scripts/` | **PORT** | 13 steps → handler catalog. State-file resume → ProvisioningRun record. **v3: add Cosmos DB provisioning step** (missing from current 13, but BFF prereq per INVENTORY §7). |
| `Build-SpaarkeMaster.ps1` | `scripts/` | **REUSE (authoritative)** | Machine composition of 386-component solution. INVENTORY §0 source of truth. |
| `Deploy-DataverseSolutions.ps1` | `scripts/` | **REUSE + EXTEND** | Called by H6. **v3**: extend to Package Deployer invocation for dependency-ordered import per INVENTORY §1 (~10 managed solutions). |
| `Deploy-BffApi.ps1` | `scripts/` | **REUSE** | Called by H9. |
| `Deploy-Release.ps1` | `scripts/` | **REUSE + HARDEN (Gap 2)** | Called by H9. **v3**: Phase 4 must be `customerId`-driven; remove `spaarkedev1` hardcode. |
| `Validate-DeployedEnvironment.ps1` | `scripts/` | **REUSE + EXTEND (Gap 4)** | Called by H13. **v3**: extend to end-to-end acceptance gate — sample analysis, sample document upload+index, workspace-layout render, wizard field-map. |
| `Test-Deployment.ps1` | `scripts/` | **REUSE** | Smoke-test handler. |
| `Register-EntraAppRegistrations.ps1` | `scripts/` | **PORT (Gap 3)** | Basis for H3. **v3**: needs full idempotency for ~11 permission grants (INVENTORY §7 T3, not v2's stated 5); admin consent handled via H0.5 consent-callback for Model 2. |
| `Create-NewContainerType.ps1` + `Register-*.ps1` + `New-BusinessUnitContainer.ps1` | `scripts/` | **PORT + FIX (T6)** | Basis for H8. **v3**: switch to **confidential-client (app-only) token** — delegated token 403s (`public client not allowed`) per INVENTORY §10. Cert bootstrapped from KV via `Import-And-Register.ps1`. |
| `Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1` + `Deploy-*` (seed layer) | `scripts/seed-data/` + `infra/dataverse/**` | **PORT (Gap 1)** | Basis for H12a/b/c. **v3**: resolve two-source drift (`scripts/seed-data` MVP vs `infra/dataverse` R7) via declarative seed manifest. |
| `Deploy-AllIndexes.ps1` | `infrastructure/ai-search/` | **REUSE** | Invoked by H2b — 7 indexes. |
| `Decommission-Customer.ps1` | `scripts/` | **OUT OF SCOPE** (D17) | Remains operational as-is. Registry-aware teardown deferred to r2. |
| `/deploy-new-release` | `.claude/skills/` | **REUSE as-is** | Out of scope. Reference model for L3 skill UX. |

### 11.2 Infrastructure-as-Code (v3 updated)

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `customer.bicep` | `infrastructure/bicep/` | **REUSE + EXTEND** | Extend for dedicated OpenAI/Search/DocIntel/AppInsights per D3/D12 **+ Cosmos DB (v3, BFF prereq) + optional SignalR (v3)**. |
| `platform.bicep` | `infrastructure/bicep/` | **REBUILD** | Shrinks to control-plane-only: L2 App Service (v3, B2), Cosmos DB (control-plane `spaarke-provisioning`), platform KV (parameter secrets), monitoring (D12). |
| **26 Bicep modules** *(v3 count corrected, was 18)* | `infrastructure/bicep/modules/` | **REUSE** | Composable building blocks (INVENTORY §7 confirms 26). |
| `model1-shared.bicep` + `model1-customer.bicep` + `model2-full.bicep` | `infrastructure/bicep/stacks/` | **REUSE (all three first-class per v3 §3A A1)** | `model2-full` = D3 default dedicated; `model1-shared` = trial tier (§3A A1). |
| **NEW: Terraform Power Platform provider** | `infrastructure/terraform/dataverse/` *(new dir)* | **NEW** | v3 D14 — hybrid tooling per §4A. Manages Dataverse env lifecycle + application users. |
| **NEW: L2 control-plane Bicep** | `infrastructure/bicep/platform-controlplane.bicep` *(new)* | **NEW** | App Service (v3 B2) + Cosmos DB + platform KV for the L2 orchestrator. |

### 11.3 BFF Job Handler Ecosystem

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `IJobHandler` + ADR-004 | `Services/Jobs/` | **REUSE** | Provisioning handlers implement this contract. |
| 13 production handlers | `Services/Jobs/Handlers/`, `Services/Ai/Jobs/` | **REFERENCE** | Pattern exemplars for handler structure, idempotency, telemetry. |
| `JobSubmissionService` | `Services/Jobs/` | **ASSESS** | Enqueue mechanism. Provisioning may need a dedicated queue or the control plane enqueues directly. |
| `IdempotencyService` (Redis) | `Services/Jobs/` | **REUSE** | Three-level idempotency proven. |

### 11.4 Registration/Provisioning Services

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `DemoProvisioningService` (9-step) | `Services/Registration/` | **PORT** | User provisioning logic -> H11. |
| `RegistrationDataverseService` | `Services/Registration/` | **REUSE** | Cross-env token cache + multi-URL ops directly applicable to handlers. |
| `DataverseEnvironmentService` | `Services/Registration/` | **REUSE** | Reads registry records. No caching per NFR-01. |
| `GraphUserService` | `Services/Registration/` | **REUSE** | User creation, UPN generation, license assignment (D5). |
| `DemoExpirationService` | `Services/Registration/` | **CARRY-OVER (R5)** | Must migrate off `[Obsolete]` options. Not critical path. |

### 11.5 Documentation (3 generations)

| Asset | Path | Disposition |
|---|---|---|
| `ENVIRONMENT-DEPLOYMENT-GUIDE.md` (14 sections, 13 known issues) | `docs/guides/` | **MINE then SUPERSEDE** — known issues -> risk register; manual steps -> handler requirements. |
| `CUSTOMER-ONBOARDING-RUNBOOK.md` (9 sections) | `docs/guides/` | **MINE** — pre-checklist -> preflight inputs; escalation -> failure-mode design. |
| `auth-deployment-setup.md` (auth v2, 21 MUSTs) | `docs/guides/` | **REUSE** — app-settings + UAMI + Dataverse-app-user -> BFF-config handler contract. |

### 11.6 `spaarke-data-cli` (separate repo)

**Location**: `C:\code_files\SPAARKE-DATA-CLI`
**Status**: Pre-alpha scaffolding (269 lines TypeScript, 2 commits, zero implementation).
**Relevance**: The CLI's `load`/`onboard` commands (Phase 3) are the eventual customer **data import** pipeline (CSV import, legacy data migration, SPE bulk load) — **explicitly OUT of r1 scope** per §13.
**v3 design decision**: r1's config-seed layer (H12a/b/c) covers **application configuration + seed rows** (per INVENTORY §9). New customers start **empty-but-functional**; data migration is a separate follow-on project (`spaarke-data` CLI). This is the correct split — config-seed is a solution deployment concern; data-migration is a per-customer engagement concern.

### 11.7 PCF controls — v3 packaging-gap flag (per INVENTORY §4)

INVENTORY §4 documents 33 PCF folders in the repo; only **7** are in-use and shipped via `Build-SpaarkeMaster.ps1`. The remaining 26 are either feature-solution-scoped (verify per-solution), orphaned (not on any form), or retired.

**Handler implication**: H6 (solution import) MUST NOT assume all 33 controls ship. Solution packaging is authoritative — the 7 confirmed-in-use (per INVENTORY §4A: `DocumentRelationshipViewer`, `EventFormController`, `RelatedDocumentCount`, `SpeDocumentViewer`, `VisualHost`, `SemanticSearchControl`, `EventAutoAssociate`) are the base; feature solutions add more per their manifests. Phase A verification item: reconcile 33-vs-7 mapping.

---

## 12. Risk Register (v3 refreshed 2026-08-12)

Absorbed from the 13 known deployment-guide issues + r1 carry-overs + 2026-08-12 assessment findings. v3 additions R10–R15.

| ID | Risk / known issue | Source | Design must... |
|---|---|---|---|
| R1 | SPE container-type creation — `westus` billing requirement + up-to-24h replication delay. | ENV-GUIDE + INVENTORY §10 | **v3**: replication delay is **lead-time** (§9 north star), not in-pipeline wait. H8 initiates; lead-time item on customer prereq checklist. |
| R2 | Dataverse application user creation — v2 said PPAC-UI-only. | v2 finding | **v3 RESOLVED**: TF Power Platform provider `powerplatform_user` resource — fully automated (D14 v3). |
| R3 | Solution export/fix pipeline is 8 manual sed-style steps; managed-vs-unmanaged changes it (D1). | ENV-GUIDE §6 | H6 scripts export→fix→pack-managed→verify + Package Deployer; no manual edits. |
| R4 | Entra app reg — **~11 permission GUIDs** granted by hand *(v3 corrected: was 5)*; no recovery script. | ENV-GUIDE §4 + INVENTORY §7 T3 | H3 scripts grants idempotently; admin-consent is a verified gate for Model 2 (D18 consent-callback). |
| R5 | `DemoExpirationService` still binds `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment`; blocks deleting Azure config. | r1 lessons | Carry-over: migrate to registry lookup. Not critical path but tracked (Phase E). |
| R6 | Doc drift across 4 overlapping master guides (11+ deployment guides), stale env-var counts, hardcoded env names. | r1 lessons + PROJECT-UPDATE §6 Gap 4 | Consolidate to one authoritative guide + one validated env-var/app-setting manifest reconciled to BFF code `[Required]`. |
| R7 | "Validated but not wired" defect class (r1 FR-11: license parsed, never applied). | r1 lessons | Every handler's acceptance asserts value reaches its consumer. H13 checks effects, not intentions. |
| R8 | CORS localhost leakage, missing ChatModelName, max-upload-size < PCF bundle, solution import order, canvas-app deps. | ENV-GUIDE issues 1–7, 10 | Fold each into relevant handler's post-conditions + H13 validation. |
| R9 | AI Search index schema drift: JSON schema files may not match current BFF field usage. | Feedback round 1 | Phase A must audit **7 (v3 corrected)** index JSON schemas against BFF service code. |
| **R10 (v3)** | **SPE container creation 403s on delegated token** (`public client not allowed`) — production live-drift trap. | INVENTORY §10 | **T6 (§4B)**: H8 uses confidential-client (app-only) token from KV cert. |
| **R11 (v3)** | **Cosmos DB provisioning absent from current 13-step orchestrator** — BFF will not start without it. | INVENTORY §7 + PROJECT-UPDATE §6 | H2a Bicep includes Cosmos DB (serverless, partition `/tenantId`); UAMI granted Data Contributor role. |
| **R12 (v3)** | **`keyVaultReferenceIdentity` not PATCHed to UAMI** — all `@Microsoft.KeyVault(...)` refs resolve to null at App Service runtime; BFF fails silently. | INVENTORY §7 + auth-deployment-setup.md | **T1 (§4B)**: H4 post-step PATCHes `keyVaultReferenceIdentity` on both prod + staging slots. |
| **R13 (v3)** | **Config-seed layer decoupled from provisioning** (biggest gap) — solutions ship definitions, not rows → fresh env is non-functional (blank grids, unmapped wizards, dark AI, no workspace). | INVENTORY §9 + PROJECT-UPDATE §6 Gap 1 | H12 promoted from thin to H12a/b/c first-class handlers with declarative seed manifest. |
| **R14 (v3)** | **Two-source AI seed drift**: `scripts/seed-data/*.json` (2026-01 MVP) vs `infra/dataverse/**` (R7 current). | INVENTORY §9 | H12a seed manifest declares authoritative source per artifact; Phase A resolves the drift. |
| **R15 (v3)** | **TF Power Platform provider maturity**: SPs can't create `Developer`-type envs (Sandbox/Production only); SP must be admin-bootstrapped via BAP API once per tenant. | §4A + PROJECT-UPDATE §8 | Preflight H0 asserts SP is BAP-bootstrapped; profile defaults exclude `Developer` type. |
| **R16 (v3)** | **Hardcoded `spaarkedev1` in `Deploy-Release.ps1` Phase 4** — code-page deploy targets dev env regardless of `customerId`. | PROJECT-UPDATE §6 Gap 2 | H9 uses hardened Phase 4 (`customerId`-driven). |

---

## 13. Scope

### In Scope (v3 — "fully deploy a customer" scope)

**L1/L2/L3 architecture** (per v2, unchanged shape; handler count adjusted):
1. **L1 handler catalog** — **19 handlers** (v3 count: H0, **H0.5**, H1, **H2a/H2b**, H3, H4, H5, H6, H7, H8, H9, H10, H11, **H12a/H12b/H12c**, H13, H14) implementing the provisioning pipeline as idempotent `IJobHandler` implementations
2. **L2 control-plane service** — standalone **App Service** (v3 B2) with Cosmos DB state, run lifecycle, gate management, REST + AAD (v3 B1)
3. **L3 operator skill** — `/provision-environment` Claude Code skill (D16) invoking L2 REST API
4. **ProvisioningRun data model** — Cosmos DB `spaarke-provisioning` database with `runs` container (D13), enumerated shapes (v3 I2)
5. **Registry extension** — **9 new columns** on `sprk_dataverseenvironment` (v3: adds `sprk_currentrunid`, `sprk_tenancymodel`, `sprk_tenantid`)

**Four gaps closure** (PROJECT-UPDATE §6):
6. **Gap 1 — Config-seed as first-class** (H12a/b/c) with declarative manifest resolving two-source AI seed drift
7. **Gap 2 — `Deploy-Release.ps1` Phase 4 hardening** (`customerId`-driven; no `spaarkedev1` hardcode)
8. **Gap 3 — Entra app registrations + Dataverse App User automation + Model 2 consent-capture** (H3 idempotent 11-grant flow, H10 TF-driven, H0.5 consent-callback per D18)
9. **Gap 4 — Single end-to-end acceptance gate + doc consolidation** (extended `Validate-DeployedEnvironment.ps1`; one authoritative deploy guide + one env-var/app-setting manifest)

**Fast-follow engineering items** (PROJECT-UPDATE §10):
10. **Per-tenant token-metering layer** (D19 — APIM gateway or app-level custom metric on `tenantId`)
11. **SPE 403 fix** — confidential-client (app-only) token per T6
12. **Cosmos DB provisioning** added to orchestrator (H2a — BFF prereq)
13. **Silent-failure trap catalog** (§4B: T1–T6) baked into handler post-conditions

**Tooling & infrastructure** (v3):
14. **Terraform Power Platform provider adoption** (hybrid: Bicep for Azure, TF for Dataverse env lifecycle per D14 v3 & §4A)
15. **Managed-solution packaging** — scripted export/fix/pack-managed/verify pipeline (D1) via Package Deployer
16. **Parameter model** — hybrid profiles (D15) + §3A A1 trial-tier profile + full parameter spec (§10.2) with KV-ref secrets (B3)
17. **AI Search index provisioning** — 7 indexes (v3 corrected) via H2b `Deploy-AllIndexes.ps1`
18. **`platform.bicep` rebuild** — shrink to control-plane-only resources (D12)
19. **`customer.bicep` extension** — add per-customer OpenAI + AI Search + Doc Intelligence + App Insights + **Cosmos + optional SignalR** (v3)
20. **`model1-shared.bicep` as first-class stack** (§3A A1 trial-tier)

**Acceptance:**
21. **E2E dry run** — stand up one brand-new environment using only the new pipeline; reach `Setup Status = Ready`; validate all silent-fail traps cleared

### Out of Scope (v3 confirmed by owner 2026-08-12)

- **Data migration** — CSV/legacy import, SPE bulk load — belongs to `spaarke-data` CLI (separate project). New customer starts empty-but-functional.
- **Per-customer isolation for Office add-ins / external SPA / M365 Copilot agent** — currently single-shared per INVENTORY §8; per-customer SWA/portal/agent automation is a follow-on.
- **Registry-aware decommission pipeline** (D17) — existing `Decommission-Customer.ps1` remains operational as-is; deferred to r2.
- **Fleet management web app** (D13) — read-only Cosmos UI deferred; r1 fleet visibility comes from `sprk_dataverseenvironment` + `sprk_currentrunid`.
- **Spaarke Assistant** front end (design-acknowledged, built later).
- Changes to the **ongoing release process** (`/deploy-new-release` consumed as-is).
- **Disaster recovery / backup** automation.
- **CI/CD workflow changes** (existing workflows consumed as-is; handlers call underlying scripts directly).

### Carry-Overs (tracked, not critical path)

- R5: `DemoExpirationService` migration off `[Obsolete]` options → registry lookup (Phase E)
- R6: Doc drift fixes rolled into Gap 4 doc consolidation (Phase A)
- r1 live-provisioning sign-off (criteria 5/8/9/11) folded into E2E dry run (Phase F)

---

## 14. Phasing (v3 refreshed 2026-08-12)

| Phase | Content | Depends on | Notes |
|---|---|---|---|
| **A** | Doc consolidation (Gap 4 — one authoritative guide + env-var/app-setting manifest) + AI Search index schema audit (R9) + INVENTORY §12 verification backlog (33-vs-7 PCF, 87-entity roster export, two-source AI seed drift resolution, managed-solution export coverage) + doc-drift fixes (R6) | — | Parallel with B |
| **B** | Gap automation scripts — hardened & idempotent (Entra apps 11-grant H3 per R4, SPE H8 confidential-client per T6, solution export/fix managed H6, `Deploy-Release.ps1` Phase 4 hardening per Gap 2, Cosmos DB provisioning added per R11) | — | Parallel with A |
| **B'** *(v3, new)* | TF Power Platform provider adoption — SP BAP bootstrap (per R15), TF module for `powerplatform_environment` + `powerplatform_user`, integration tests against Sandbox env | — | Parallel with A + B; unlocks H5 + H10 |
| **C** | Registry schema extension (9 columns per §6.1) + ProvisioningRun data model (Cosmos, shapes per §6.2) + `customer.bicep` extension (Cosmos + SignalR + `model1-shared.bicep` first-class) + `platform.bicep` rebuild (L2 App Service + Cosmos + platform KV) + **L2 control-plane** (REST API + AAD per B1, App Service per B2, concurrency per I5, crash-recovery per I6) integrating all 19 handlers | A, B, B' | Core build phase |
| **C'** *(v3, new)* | H12a/b/c config-seed manifest implementation — declarative seed authoritative-source table resolving R14 drift; all seeders idempotent + resumable; H14 integration wiring (2× Exchange policies per T4, Graph webhooks, S2S consent) | A (drift resolution), C | Highest functional payoff (Gap 1) |
| **D** | `/provision-environment` operator skill + L2 REST API integration + Model 2 consent-capture landing (BFF endpoint per D18) + per-tenant token-metering layer (D19) | C | L3 + fast-follow |
| **E** | `DemoExpirationService` migration + Azure legacy-config deletion + verification | — | Parallel; BFF task, FULL rigor (per CLAUDE.md §10 BFF Hygiene checklist) |
| **F** | E2E dry run: new environment end-to-end (Model 2 + Model 1 trial both verified) + all silent-fail traps cleared + r1 live sign-off items + wrap-up | C, C', D, E | Acceptance |

**Parallelism**: A, B, B', E can start immediately in parallel. C waits on {A, B, B'}. C' waits on {A drift resolution, C}. D waits on C. F is acceptance.

---

## 15. Success Criteria (v3 refreshed 2026-08-12)

**North star**: automated provisioning completes in **<1h of pipeline runtime**; customer is production-ready within **one business day** of admin consent + Azure quota being in place (per PROJECT-UPDATE §9). Three items that blow past a day are lead-time not compute: Azure quota / OpenAI region capacity (1–3 days), SPE container-type replication (up to 24h), customer admin consent + security review (customer-dependent). Front-load lead-time items in preflight.

1. One authoritative deployment guide covers all provisioning phases + one validated env-var/app-setting manifest reconciled to BFF code `[Required]` annotations (Gap 4)
2. Each of the 19 handlers (H0, H0.5, H1, H2a/b, H3–H11, H12a/b/c, H13, H14) is idempotent, independently testable, and reports its outcome to the Cosmos run record
3. The L2 control plane sequences handlers, manages gates, enforces per-customer serialization (I5), and auto-resumes orphaned runs on startup (I6)
4. All Gap 3 items — Entra app registration (11 grants), SPE container type (confidential-client fix per T6), Dataverse App User (TF-driven per D14), Model 2 consent-capture (D18) — run unattended and idempotently
5. A brand-new environment reaches `Setup Status = Ready` via the new pipeline; extended `Validate-DeployedEnvironment.ps1` exits 0 asserting end-to-end effects (sample analysis + sample document upload+index + workspace-layout render + wizard field-map)
6. All 6 silent-fail traps (§4B T1–T6) verified cleared by their owning handler's post-condition
7. `DemoProvisioning__Environments__*` and `__DefaultEnvironment` deleted from Azure; expiration flow verified working (R5)
8. `/provision-environment` skill executes the full flow with confirmation gates and produces a handoff report
9. ProvisioningRun records in Cosmos are queryable for fleet status (how many environments, in what state); `sprk_currentrunid` visible on `sprk_dataverseenvironment`
10. All **7** AI Search indexes (v3 corrected) created per customer with standardized naming and verified field alignment (Phase A audit item cleared)
11. All **7** per-customer Dataverse environment variables set and validated (no hardcoded URL fallbacks); reconciled with INVENTORY §9
12. **Model 2** (dedicated per D3): per-customer AI resources (OpenAI, AI Search, Doc Intelligence, Cosmos) deployed isolated
13. **Model 1** (trial per §3A A1): shared fixed-floor tier deployed; per-tenant token-metering layer (D19) enforces `tokenBudgetMonthlyUSD`
14. **BFF publish size** ≤60 MB compressed (CLAUDE.md §10 NFR-01); Phase E DemoExpirationService migration verifies ~0 MB delta

---

## 16. Resolved Design Decisions

**v2 resolutions (Q1–Q6, feedback round 1, 2026-06-16)**. Q3 answer superseded by v3 D14.

| Q | Question | Resolution | Locked Decision |
|---|----------|-----------|----------------|
| Q1 | Control-plane placement & fate of `platform.bicep` | **Standalone service** in platform RG. `platform.bicep` shrinks to control-plane-only. Per-customer AI resources move to `customer.bicep`. **v3 B2 refines**: App Service (not Container App). | D12 |
| Q2 | ProvisioningRun store | **Cosmos DB serverless** (`spaarke-provisioning` database, `runs` container). | D13 |
| Q3 | Headless Dataverse application-user creation (H10) | **v2**: semi-automated with PPAC fallback. **v3 SUPERSEDED**: fully automated via TF Power Platform provider. | D14 (v3) |
| Q4 | Decommission scope | **Out of scope.** Existing `Decommission-Customer.ps1` remains operational. Deferred to r2. | D17 |
| Q5 | Environment profiles vs pure parameters | **Hybrid profiles.** Named profiles set defaults; every parameter overridable. **v3 §3A A1 expands**: trial-tier profile added. | D15 |
| Q6 | MCP server runtime & auth | **No separate MCP server in r1.** Skill invokes L2 REST API. **v3 B1 refines**: REST + AAD bearer + `Operator`/`Reader` app-roles. | D16 |

**v3 resolutions (2026-08-12 assessment + v2-critical-review open items)**:

| Q | Question | Resolution | Locked in |
|---|----------|-----------|----------|
| **B1** | L2 protocol + auth model | REST API + AAD bearer + audience `api://spaarke-provisioning-controlplane-{env}` + `Operator`/`Reader` app-roles | §4.2 v3 |
| **B2** | L2 hosting technology | App Service (parity with BFF; Container Apps rejected for provisioning-cadence workloads) | §4.2 v3 |
| **B3** | Secrets at rest in Cosmos `parameters` | Secrets stored as KV URI refs in platform KV; resolved at handler runtime via UAMI; no cleartext in Cosmos | §6.2 + §10.2 v3 |
| **B4** | H8 long-wait pattern | Recast: SPE replication up to 24h is **lead-time** (§9 north star), not in-pipeline wait; H8 initiates via confidential-client (T6) | §4.1 v3 + §12 R1 |
| **B5** | AI Search index provisioning placement | Split into standalone handler H2b using existing `Deploy-AllIndexes.ps1` | §4.1 v3 + §8.5 |
| **I1** | H14 post-deploy integration wiring | Enumerated: 2× Exchange ApplicationAccessPolicies (BFF + UAMI per T4), Graph webhook subscriptions per Communication/Email module, service endpoint webhooks, S2S consent flows | §4.1 v3 |
| **I2** | `gateStates` + `interStepState` shapes | Enumerated per-key structures in §6.2 | §6.2 v3 |
| **I3** | Idempotency `-v{n}` semantics | Deterministic content hashes / semantic versions: `{bicepVer}` = git SHA, `{solutionVer}` = manifest hash, `{configVer}` = seed hash, `{buildId}` = CI build | §4.1 v3 preamble |
| **I5** | Concurrency model | Optimistic concurrency on `sprk_dataverseenvironment.sprk_currentrunid`; same-customer serialized, cross-customer parallel | §4.2 + §6.1 v3 |
| **I6** | L2 crash recovery | On startup, scan Cosmos for orphaned `Running`/`WaitingOnGate` runs older than 2× median handler duration; re-schedule from `currentPhase` | §4.2 v3 |
| **D3-tension** | Fixed-floor cost vs D3 dedication | Path A amendment: keep D3 default + add Model 1 shared trial tier + per-tenant token-metering layer | §3A (v3) |
| **Tooling** | TF Power Platform provider adoption | Hybrid: keep Bicep for Azure, add TF for Dataverse env lifecycle | §4A + D14 (v3) |
| **Self-service** | Customer-tenant consent capture | BFF exposes `/api/onboarding/consent-callback`; H0.5 captures `tid` and triggers pipeline | §4.1 H0.5 + D18 (v3) |

---

## 17. Placement Justification (CLAUDE.md section 10)

- **New scripts + skill + procedure doc**: `scripts/`, `.claude/skills/`, `docs/procedures/` — no BFF impact.
- **Provisioning handlers**: Register in the **control-plane service**, not the BFF. The control plane is Spaarke-internal fleet management (D3, D8, D12); the BFF is per-customer. Zero BFF DI impact.
- **Control-plane service**: New standalone service in `rg-spaarke-platform-{env}`. Not the BFF. Cosmos DB for state. No shared-resource conflict.
- **Only BFF changes** (v3 — two additions vs v2):
  - **Phase E** — `DemoExpirationService` migration (R5 carry-over): modifies an existing registered service to use `DataverseEnvironmentService`. No new endpoints, packages, or DI registrations. Expected publish-size delta: ~0.
  - **Phase D** — **BFF `/api/onboarding/consent-callback` endpoint** (v3, D18) for Model 2 self-service consent capture. NEW endpoint + one new handler. Expected publish-size delta: ~0.1 MB (single controller + verification helper).
  - Both changes MUST follow the CLAUDE.md §10 BFF Hygiene checklist: load `.claude/constraints/bff-extensions.md`, publish-size verification (60 MB ceiling), test update obligation, no new HIGH CVEs.
- **Registry schema extension**: Dataverse-only (**9 new columns v3**, was 6 v2 — adds `sprk_currentrunid`, `sprk_tenancymodel`, `sprk_tenantid`).
- **`customer.bicep` extension**: Infrastructure-as-Code only. Adds per-customer AI resources (OpenAI, AI Search, Doc Intelligence, App Insights) **+ Cosmos DB (v3, R11) + optional SignalR (v3)** — no BFF code changes.
- **`platform.bicep` rebuild**: Infrastructure-as-Code only. Shrinks to control-plane resources (L2 App Service, Cosmos, platform KV, monitoring).
- **`model1-shared.bicep`** (v3 §3A A1): first-class trial-tier composition using shared fixed-floor resources (App Service Plan, OpenAI, AI Search) + dedicated for everything else.
- **NEW: Terraform Power Platform provider directory** (v3, `infrastructure/terraform/dataverse/`): separate IaC dialect from Bicep; scoped strictly to Dataverse env + application user lifecycle per §4A.
- **NEW: Per-tenant token-metering layer** (v3, D19): either APIM gateway or app-level custom App-Insights metric keyed on `tenantId`. Placement TBD in D-phase implementation; either way, minimal BFF DI impact (single tracker service).

---

## 18. Open Items for Phase A Audit (v3 refreshed 2026-08-12)

These items require detailed verification during Phase A (doc consolidation + audit) before implementation begins. **v3**: cross-referenced with INVENTORY §12 verification backlog.

1. **AI Search index schema audit** — Compare **7** JSON schema files (v3 corrected count) in `infrastructure/ai-search/` against current BFF service field usage. Confirm field names, types, vector dimensions, and filterable/searchable attributes match. Flag any schema drift (R9).
2. **Document Intelligence feature verification** — Confirm `prebuilt-layout` is the only model in use. Verify `DocumentParserRouter` file-type routing is complete and accurate. Check if any custom models are planned.
3. **BFF app settings completeness** — Verify the 26 `IOptions<T>` configuration sections (§10.4) against `appsettings.template.json`. Confirm all deploy-time tokens are documented. Identify any settings that should move from literal values to Key Vault references. **v3**: reconcile "~25 settings found only by BFF startup exceptions" per PROJECT-UPDATE §6 Gap 4 — every one MUST have a corresponding `[Required]` annotation or documented default.
4. **Dataverse environment variable usage** — Confirm **7** per-customer values (v3 corrected) plus the additional 14 solution env-var definitions are correctly categorized (per-customer vs seed vs BFF-only). Verify no migration to Azure App Configuration is in progress.
5. **Index naming standardization** — The dev environment uses `spaarke-invoices-dev` (non-standard suffix). Standardize to `spaarke-invoices-index` or confirm the `-dev` suffix is intentional for dev-only isolation.
6. **(v3, from INVENTORY §12) Export the complete named 87-entity roster** from a live env (`EntityDefinitions?$filter=startswith(LogicalName,'sprk_')`) and pin it in INVENTORY §2.
7. **(v3, from INVENTORY §12) Reconcile 33 PCF folders → 7 in-use** — map each of the remaining 26 to the feature solution that packs it (or mark retired). Confirms H6 solution-import expectations.
8. **(v3, from INVENTORY §12) Resolve the two-source AI seed drift** — `scripts/seed-data` MVP vs `infra/dataverse` R7 → single authoritative source per artifact type declared in H12a seed manifest.
9. **(v3) TF Power Platform provider maturity** — verify provider covers all resources H5/H10 need (`powerplatform_environment`, `powerplatform_user`, security-role assignment); assess community adoption + Microsoft's roadmap; confirm SP BAP-bootstrap steps are documented.
10. **(v3) Silent-fail trap verification** — for each of §4B T1–T6, author the verification command as the handler's post-condition assertion (not a test in a separate suite); code-review confirms the assertion runs on every provisioning run.

---

## 19. References (v3 added)

**Companion docs** (authoritative supplements, updated more frequently than this design):
- [`PROJECT-UPDATE-2026-08-12.md`](PROJECT-UPDATE-2026-08-12.md) — 2026-08-12 six-workstream assessment, cost economics, D3 tension analysis, gap analysis, fast-follow list
- [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) — machine-hardened bill-of-materials (386 solution components, 87+ entities, Azure stamp, config/seed layer, verification backlog)
- [`discovery/phase-0-discovery-report.md`](discovery/phase-0-discovery-report.md) — original Phase 0 findings

**Load-bearing spine assets** (in-repo):
- `scripts/Provision-Customer.ps1` — 13-step orchestrator (basis for handler catalog)
- `scripts/Build-SpaarkeMaster.ps1` — machine composition of 386-component solution (INVENTORY source of truth)
- `scripts/Deploy-Release.ps1` + `Deploy-Platform.ps1` + `Deploy-BffApi.ps1` + `Decommission-Customer.ps1` + `Validate-DeployedEnvironment.ps1` — release/platform/BFF/teardown/validate
- `scripts/seed-data/Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1` + module seeders (H12a/b/c basis)
- `infrastructure/bicep/**` (26 modules + `platform.bicep` / `customer.bicep` / `model1-shared.bicep` / `model2-full.bicep`)
- `infrastructure/ai-search/Deploy-AllIndexes.ps1` (H2b — 7 indexes)

**Guides to consolidate into one authoritative** (Gap 4 / Phase A):
- `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`
- `docs/guides/CUSTOMER-ONBOARDING-RUNBOOK.md`
- `docs/guides/auth-deployment-setup.md`
- `docs/guides/MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md`
- `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md`

**Related / superseded projects**:
- `projects/spaarke-environment-factory-r1/` — superseded (this project inherits the mission)
- `spaarke-environment-provisioning-app` (r1, complete PR #390) — user-provisioning + registry foundation
- `projects/production-environment-setup-r2/` — env-agnostic config; feeds §10.4 BFF app-settings work
- `projects/spe-multi-tenant-architecture-r1/` — multi-issuer BFF; feeds Model 2 self-service (D18)
- `projects/spaarke-demo-data-setup-r1/` (`spaarke-data` CLI) — data migration follow-on (v3 out-of-scope)

**Architecture / ADR anchors**:
- ADR-004 (async job contract), ADR-014 (data model), ADR-020 (model version pinning), ADR-027 (subscription isolation + 2026-06-02 unmanaged-solution amendment), ADR-028 (auth v2), ADR-032 (Null-Object kill-switch), ADR-034 (notifications spine), ADR-039 (single AI routing surface)
- `docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md` (Model 1/2)
- `docs/architecture/AI-ARCHITECTURE.md`

---

## 20. CHANGELOG

### v3 — 2026-08-12 (post-assessment refresh)

**Trigger**: project paused June 2026; owner assessment 2026-08-12 (PROJECT-UPDATE-2026-08-12.md + COMPONENT-INVENTORY.md) surfaced 6 items v2 missed and 1 locked decision (D3) worth re-validating.

**Changes**:
- **Header**: Draft v3, companion-doc references, revision line for 2026-08-12
- **§3**: D14 rewritten (TF Power Platform provider replaces PPAC semi-auto fallback); D18 added (BFF as consent-capture onboarding client); D19 added (per-tenant token-metering as no-regret investment)
- **§3A NEW**: ADR-Tensions — D3 Path A amendment (shared trial tier + metering layer + architectural cost controls) per CLAUDE.md §6.5
- **§4A NEW**: Tooling stack table — Bicep + TF + PS + Package Deployer + confidential-client SPE, with rejected alternatives
- **§4.1**: Handler catalog rewritten — H0.5 added (consent-capture); H2 split into H2a/H2b (infra + AI Search indexes); H5 + H10 TF-driven; H8 confidential-client fix (T6); H9 hardened (`spaarkedev1`); H12 split into H12a/H12b/H12c (AI seed + app config + runtime refs); H14 enumerated (2× Exchange + webhooks + S2S). Handler DAG added. I3 idempotency `{schemaVer}` semantics defined.
- **§4B NEW**: Silent-failure trap catalog (T1–T6) with owning handler + verification command
- **§4.2**: B1 resolved (REST + AAD bearer + Operator/Reader roles); B2 resolved (App Service); I5 resolved (per-customer serialization via `sprk_currentrunid`); I6 resolved (crash-recovery scan). REST endpoint table.
- **§6.1**: Added `sprk_currentrunid` (I5), `sprk_tenancymodel` (A1), `sprk_tenantid` (D18)
- **§6.2**: `parameters` field now KV-URI refs only (B3); `gateStates` + `interStepState` shapes enumerated (I2); `currentPhase` typed as string (sub-handlers)
- **§7**: INVENTORY §7 declared authoritative; UAMI + Cosmos + SignalR added; shared-vs-dedicated table
- **§8**: 8→7 index count corrected; H2b promoted to first-class
- **§9.1**: Sign-in audience `AzureADMultipleOrgs` (Model 2 self-service); client secret as KV ref
- **§9.2**: UAMI (was system-assigned MI); ~11 Graph app-roles (was 7); T3 verification
- **§9.3**: TF-driven H10 with T2 verification
- **§9.4**: Both Exchange policies mandatory (T4); propagation reframed as lead-time
- **§10.1**: Model 1 shared trial profile added; profile names normalized (`-model2` / `-model1-trial`)
- **§10.2**: `clientSecret` → `clientSecretKvRef` (B3); `tenancyModel` + `tokenBudgetMonthlyUSD` added
- **§10.3**: 8→7 env-var reconciliation with INVENTORY §9 as source of truth
- **§11.1–11.7**: Asset disposition refreshed; 26 Bicep modules (was 18); Model 1 stacks first-class; TF added; new §11.7 PCF gap
- **§12**: R10–R16 added (SPE 403, Cosmos absence, `keyVaultReferenceIdentity`, config-seed decouple, AI seed drift, TF maturity, `spaarkedev1` hardcode)
- **§13**: Scope re-stated as 21 in-scope items grouped by concern; out-of-scope confirmed (data migration, per-customer SWA/portal/agent, decommission, fleet UI)
- **§14**: Phasing refreshed with new B' (TF adoption) + C' (H12a/b/c config-seed) sub-phases
- **§15**: 14 success criteria (added trap-verified, 7-index/env-var reconciliation, Model 1 + Model 2 both verified, publish-size compliance, north-star framing)
- **§16**: v3 resolutions table (B1–B5, I1–I3, I5–I6, D3-tension, Tooling, Self-service)
- **§17**: Two BFF changes now (consent-callback endpoint added); 9-column registry (was 6); Model 1 stack + TF + metering layer placement
- **§18**: Open items 6–10 added (INVENTORY §12 verification backlog + TF maturity + trap verification)
- **§19 NEW**: References — companion docs, spine assets, guides to consolidate, related projects, ADR anchors
- **§20 NEW**: this CHANGELOG

**What did NOT change**:
- 3-layer architecture shape (L1 handlers + L2 control plane + L3 skill)
- ADR-004 resolution (§5.1) — individual handlers are IJobHandler; L2 orchestrates
- ADR-010/017 posture (§5.2/5.3) — control plane's DI is separate from BFF's; ProvisioningRun ≠ per-handler job status
- D1 (managed solutions), D2 (two targets), D3 (dedicated default — with §3A amendment), D4 (subscription per customer), D5 (Spaarke buys licenses), D6 (B2B vs Native identity), D7 (consumption SKUs), D8 (build L1→L2→L3), D9 (Claude Code as authorized MCP client), D10 (gates verified not inferred), D11 (idempotent + resumable), D12 (control plane placement), D13 (Cosmos as run store), D15 (hybrid profiles), D17 (decommission out of scope)

### v2 — 2026-06-16 (feedback round 1)
Resource inventory, identity spec, config capture, Q1–Q6 resolved → D12–D17 locked.

### v1 — 2026-06-15 (initial draft)
Superseded `spaarke-environment-factory-r1` design; captured Phase 0 discovery + D1–D11.

---

*End of design specification v3. Next step: owner review, then `/design-to-spec` → `/project-pipeline`.*
