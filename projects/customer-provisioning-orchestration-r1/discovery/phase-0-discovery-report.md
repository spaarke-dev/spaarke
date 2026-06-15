# Phase 0 — Discovery Report
## Customer Provisioning & Deployment Orchestration

> **Project**: customer-provisioning-orchestration-r1
> **Phase**: 0 (Discovery) — design engagement, no implementation code
> **Created**: 2026-06-13
> **Author**: Ralph Schroeder / Claude Code
> **Gate**: Phase 1 (draft ADR + design specs) does NOT begin until this report is reviewed and approved.

---

## 0. Purpose & method

This report inventories what Spaarke already has for standing up and deploying an environment, dispositions each asset against the locked three-layer target architecture (what becomes a Layer-1 handler vs. what gets rebuilt vs. what is reused as-is), surfaces the risks and known issues the design must absorb, and lists the architectural questions the Phase 1 ADR must resolve. It does not propose an implementation.

**Evidence base**: direct inspection of the `spaarke-environment-provisioning-app` worktree (r1, merged to master 2026-06-12 via PR #390) — `scripts/`, `infrastructure/bicep/`, `docs/guides/`, the BFF job-handler ecosystem under `src/server/api/Sprk.Bff.Api/Services/Jobs/`, ADR-004, and the r1 lessons-learned. Asset inventory cross-checked against the superseded `spaarke-environment-factory-r1/design.md` lifecycle matrix.

---

## 1. Locked decisions (carried verbatim — do not relitigate)

These are inputs, not findings. The design must conform to them.

| # | Decision | Design implication |
|---|----------|--------------------|
| D1 | **Managed solutions** for all provisioned customer environments (ADR revision in flight; assume managed). Unmanaged stays dev-only. | Solution export/fix pipeline must produce **managed** packages. New ADR records the managed-for-customer position. |
| D2 | **One deployment package, two targets.** Only variable = target tenant (Spaarke vs customer). Per-customer app registrations in **both** models. | Identity topology identical across models; tenant is a run parameter, not a code fork. |
| D3 | **No shared resources between customers.** One BFF instance per customer env. Dedicated per-customer: Azure OpenAI, AI Search index, Document Intelligence, Service Bus queues, Redis, Key Vault scope, App Insights. | Control plane is a **separate Spaarke-internal service**, not any customer BFF. Bicep deploys a full per-customer stack. |
| D4 | **Azure subscription per customer** = isolation + billing unit. Subscription target is a first-class package parameter: `SpaarkeOwned` (pass-through billing, default) \| `CustomerOwned` (Azure Lighthouse delegation, direct MS billing). | Preflight + a gate verify subscription access / Lighthouse delegation before infra steps. |
| D5 | **No bring-your-own-license.** Spaarke purchases user licenses for Spaarke-hosted customers. | License assignment handler uses Spaarke-procured SKUs (builds on r1 FR-11 per-env license resolution). |
| D6 | **Two identity presets**: `B2BGuest` (customer IT configures cross-tenant access) \| `NativeAccount` (low-IT-friction). Identity mode is a per-customer parameter; everything downstream identical. | Identity handler branches only at user-creation; gates differ (B2B needs consent verification). |
| D7 | **Consumption SKUs wherever possible** (OpenAI standard deployments, Doc Intelligence, Container Apps consumption, Cosmos serverless). Model deployment versions **pinned per ADR-020**. | Bicep parameter defaults favor consumption tiers; model versions are explicit pinned inputs. |
| D8 | **Three-layer architecture, built in order**: L1 deterministic handlers (ADR-004 job contract) → L2 control-plane API + MCP server → L3 swappable front ends. | L1 + L2 are invariant; L3 is replaceable. Build sequence is L1 first. |
| D9 | **Claude Code is an authorized internal MCP client** of the control plane. Never a runtime product component, never customer-facing. | The operator skill (L3) calls L2 MCP tools; it holds no provisioning logic. |
| D10 | **Gates verified, not inferred.** Orchestrator independently verifies gate state (consent granted, Lighthouse delegation accepted) against Graph/ARM before advancing. ProvisioningRun is the system of record; no front-end narration is authoritative. | Each gate = an explicit verification handler hitting Graph/ARM, writing result to the run record. |
| D11 | **Every step idempotent and resumable.** Failed runs resume; they do not restart. | Maps cleanly onto ADR-004 (idempotent handlers, at-least-once, deterministic idempotency keys). |

---

## 2. Asset inventory & disposition

Disposition legend: **PORT** = logic becomes/feeds a Layer-1 handler · **REUSE** = consumed as-is by a handler · **REBUILD** = concept kept, form changes · **NEW** = does not exist yet.

### 2.1 Orchestration & step logic

| Asset | Path | What it does | Disposition |
|-------|------|--------------|-------------|
| `Provision-Customer.ps1` | `scripts/` | 13-step end-to-end customer onboarding; idempotent, state-file resume, `-WhatIf`, `-ResumeFromStep`, `-SkipDataverse`. Param surface already includes CustomerId, TenantId, ClientId/Secret-or-Cert, EnvironmentName, Location, BFF URLs/IDs, OpenAI endpoint, DataverseRegion. | **PORT** — its 13 steps are the canonical handler catalog (see §3). The script's resume/state-file mechanism is the proto-ProvisioningRun; the control plane replaces the state file with the run record. |
| `Decommission-Customer.ps1` | `scripts/` | Safe teardown: BFF registry, SPE containers, Dataverse env, resource group; `-DryRun`/`-Force`/soft-delete purge. | **PORT** (later phase) — teardown handlers mirror provisioning handlers. Open question Q4 on scope. |
| `Deploy-DataverseSolutions.ps1` | `scripts/` | Import solutions in dependency order; upgrade-aware; verification. | **REUSE** — invoked by the solution-import handler. Must be confirmed managed-capable (D1). |
| `Deploy-BffApi.ps1` | `scripts/` | Build Release → zip → slot deploy → health check → swap → auto-rollback. | **REUSE** — invoked by the BFF-deploy handler. |
| `Validate-DeployedEnvironment.ps1` | `scripts/` | Verifies 7 Dataverse env vars, BFF `/healthz`+`/ping`, CORS origin, dev-value leakage. Exit 0/1. | **REUSE** — the validation gate handler wraps it (exit-code → gate state). |
| `Test-Deployment.ps1` | `scripts/` | Post-provision smoke tests (BFF, Dataverse, SPE, AI, Redis, Service Bus). | **REUSE** — smoke-test handler. |
| `Deploy-Release.ps1` + `/deploy-new-release` skill | `scripts/`, `.claude/skills/` | Interactive ongoing-release orchestration (build → BFF → solutions → web resources → validate → tag). | **REUSE as-is** — out of scope to change; the provisioning pipeline ends where release cadence begins. Reference model for the L3 operator skill's UX. |

### 2.2 Infrastructure-as-code

| Asset | Path | What it does | Disposition |
|-------|------|--------------|-------------|
| `customer.bicep` | `infrastructure/bicep/` | Per-customer: Storage, Key Vault, Service Bus (4 queues), Redis. Parameterized by customerId/env/location/SKUs. | **REUSE** — already per-customer (aligns with D3). Extend params for dedicated OpenAI/Search/DocIntel/AppInsights to fully satisfy D3 + consumption-SKU defaults (D7). |
| `platform.bicep` | `infrastructure/bicep/` | Shared App Service Plan, App Service, OpenAI (3 models), AI Search, Doc Intelligence, Log Analytics, App Insights. | **REBUILD intent** — "shared platform" conflicts with D3 (no shared resources between customers). For customer environments these resources move per-customer. platform.bicep may persist only for the Spaarke-internal control plane + demo/shared surfaces. **See Q1.** |
| 24 bicep modules | `infrastructure/bicep/modules/` | app-service, key-vault, storage, service-bus, redis, openai, ai-search, cosmos-db, doc-intelligence, monitoring, private-endpoints, role-assignment, etc. | **REUSE** — composable building blocks for the per-customer stack. Cosmos serverless + OpenAI standard already expressible (D7). |
| AI Foundry / BYOK / model1/model2 stacks | `infrastructure/bicep/stacks/`, `infrastructure/byok/` | Alternative deployment models. | **ASSESS in Phase 1** — model1/model2 stacks may already encode the one-package-two-targets idea (D2); reconcile with the locked package model. |

### 2.3 Layer-1 substrate (existing job-handler ecosystem)

| Asset | Path | Relevance | Disposition |
|-------|------|-----------|-------------|
| `IJobHandler` contract + ADR-004 | `.claude/adr/ADR-004-job-contract.md`; handlers under `Services/Jobs/` | One standard job contract: idempotent handlers, deterministic IdempotencyKey, CorrelationId propagation, JobOutcome events (Completed/Failed/Poisoned), BackgroundService workers, at-least-once. **MUST NOT use Durable Functions.** | **REUSE (foundational)** — provisioning handlers implement `IJobHandler`. The contract already provides idempotency + resume semantics that satisfy D11. Idempotency-key patterns extend naturally: e.g. `provision-{customerId}-{phase}-v{n}`. |
| Existing handlers (RagIndexing, InsightsIngest, EmailAnalysis, ProfileSummary, etc.) | `Services/Jobs/Handlers/`, `Services/Ai/Jobs/` | ~15 production handlers proving the pattern at scale. | **REFERENCE** — exemplars for handler structure, idempotency service usage, telemetry. |
| `JobSubmissionService`, idempotency service, job-status persistence (ADR-017) | `Services/Jobs/` | Enqueue + dedupe + status. | **ASSESS** — provisioning runs are long-lived and multi-step (vs single-shot indexing jobs). The run record likely needs richer state than per-job status. **See Q2.** |

### 2.4 Identity, registry & user provisioning (r1)

| Asset | Path | Relevance | Disposition |
|-------|------|-----------|-------------|
| `sprk_dataverseenvironment` entity (16 cols, deployed) | Dataverse (spaarkedev1) | Central environment registry — the fleet inventory. | **REUSE + EXTEND** — fleet-level record per environment. Distinct from the per-execution ProvisioningRun (see §4). |
| Registration/approve flow, `DataverseEnvironmentService`, multi-URL `RegistrationDataverseService` | `Services/Registration/` | Reads env config fresh; cross-environment systemuser/team ops with per-URL token cache; FR-11 per-env license resolution. | **PORT/REUSE** — the cross-tenant Dataverse write plumbing is exactly what user-provisioning + env-var handlers need. License resolution (D5) already exists. |
| `Register-EntraAppRegistrations.ps1` | `scripts/` | Existing Entra app-registration scaffold. | **PORT** — basis for the app-registration handler (D2 requires per-customer app regs in both models). |
| `Create-NewContainerType.ps1`, `Check-ContainerType-Registration.ps1` | `scripts/` | Partial SPE container-type setup + verification. | **PORT** — SPE handler; must encode the westus-billing + token-propagation-wait workarounds idempotently. |

### 2.5 Documentation (3 generations to consolidate)

| Asset | Path | Disposition |
|-------|------|-------------|
| `ENVIRONMENT-DEPLOYMENT-GUIDE.md` (14 §, 13 known issues) | `docs/guides/` | **MINE then SUPERSEDE** — the 13 known issues become the risk register (§5); manual steps become handler requirements. |
| `CUSTOMER-ONBOARDING-RUNBOOK.md` (9 §) | `docs/guides/` | **MINE** — pre-onboarding checklist → preflight handler inputs; escalation procedures → failure-mode design. |
| `auth-deployment-setup.md` (auth v2, 21 MUSTs) | `docs/guides/` | **REUSE** — app-settings + UAMI + Dataverse-app-user requirements become the BFF-config handler's contract. |

---

## 3. Candidate Layer-1 handler catalog (derived from Provision-Customer.ps1 + locked decisions)

Indicative only — the Phase 1 handler-catalog spec finalizes contracts. Ordering reflects dependency, not necessarily a strict sequence (some parallelizable).

| # | Handler (proposed) | Source logic | Gate before/after | Idempotency key (proposed) |
|---|--------------------|--------------|-------------------|-----------------------------|
| H0 | Preflight / validate inputs | Provision-Customer step 1 + runbook checklist | — | `preflight-{customerId}` |
| H1 | Subscription readiness | NEW (D4) — verify SpaarkeOwned access or CustomerOwned Lighthouse delegation via ARM | **Gate: Lighthouse delegation accepted** (CustomerOwned) | `subready-{customerId}` |
| H2 | Resource group + infra (per-customer Bicep) | step 2–3, customer.bicep + modules | — | `infra-{customerId}-v{n}` |
| H3 | Entra app registrations (per-customer, both models) | Register-EntraAppRegistrations.ps1 (D2) | **Gate: admin consent granted** (verified via Graph) | `appreg-{customerId}` |
| H4 | Key Vault secrets population | step 4 | — | `kv-{customerId}-v{n}` |
| H5 | Dataverse environment creation | step 5–6 (Power Platform Admin API) | — | `dvenv-{customerId}` |
| H6 | Solution export/fix (managed) + import | Export (NEW, D1) + Deploy-DataverseSolutions.ps1 | — | `solimport-{customerId}-v{solutionVersion}` |
| H7 | Dataverse environment variables (7) | step 8 | — | `envvars-{customerId}-v{n}` |
| H8 | SPE container type + root container | Create-NewContainerType.ps1 | (async wait for token propagation) | `spe-{customerId}` |
| H9 | BFF deploy + app settings | Deploy-BffApi.ps1 + auth-deployment-setup | — | `bff-{customerId}-v{buildId}` |
| H10 | Dataverse application user (UAMI) | manual today (PPAC) — **see Q3** | — | `appuser-{customerId}` |
| H11 | User provisioning (identity preset) | r1 registration flow (D6) | **Gate: B2B consent** (B2BGuest only) | `users-{customerId}` |
| H12 | Config + starter-data seeding | per-module seed scripts (scope-limited; see §6) | — | `seed-{customerId}-v{n}` |
| H13 | Validation gate | Validate-DeployedEnvironment.ps1 (exit 0/1) | **Gate: validation passed → registry Ready** | `validate-{customerId}-v{n}` |
| H14 | Post-deploy integration wiring | NEW — integrations named in brief | — | `integrations-{customerId}` |

---

## 4. Data model finding — ProvisioningRun vs registry (schema split)

The brief names the **ProvisioningRun** record as the system of record (D10). r1 delivered the **`sprk_dataverseenvironment`** registry. These are two different things and the design must not conflate them:

- **`sprk_dataverseenvironment`** = the *fleet inventory* — one durable record per environment (its URLs, resources, identity config, current `setupstatus`). Long-lived.
- **ProvisioningRun** = one *execution* of the pipeline against a target — phase states, gate verifications, logs, attempt counts, resume cursor. Possibly multiple runs per environment over time (initial provision, re-provision, repair).

Likely shape: a new `sprk_provisioningrun` entity (or equivalent control-plane store) with a lookup to `sprk_dataverseenvironment`. Whether the run store lives in Dataverse vs the control-plane service's own store (Cosmos serverless, per D7) is **Q2**. r1's registry-extension columns (subscription, RG, app-service, KV, container-type) belong on the **environment** record, not the run.

---

## 5. Risk register (absorbed from the 13 known issues + r1 carry-overs)

| ID | Risk / known issue | Source | Design must… |
|----|--------------------|--------|--------------|
| R1 | SPE container-type creation requires PowerShell 5.1 (SPO Management Shell), billing must use `westus` not `westus2`, and a 10–30 min Graph token-propagation wait. | ENV-GUIDE §9 | Make H8 idempotent + async-wait-aware; encode the region + wait workarounds; expose as a verified gate, not a fixed sleep. |
| R2 | Dataverse application user creation is **PPAC-UI-only** (no documented API/CLI). | ENV-GUIDE §11 | Resolve H10: confirm whether the Admin/BAP API or `pac admin` can create the app user headlessly, else mark H10 a **manual-by-design gate** the operator clears. **Q3.** |
| R3 | Solution export/fix pipeline is 8 manual sed-style steps; error-prone; managed-vs-unmanaged now changes it (D1). | ENV-GUIDE §6 | H6 must script export→fix→**pack-managed**→verify; no manual edits. |
| R4 | Entra app reg = 11 permission GUIDs granted by hand; no recovery script. | ENV-GUIDE §4 | H3 scripts grants idempotently; admin-consent is a verified gate (D10). |
| R5 | `DemoExpirationService` still binds `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment`; blocks deleting `DemoProvisioning__Environments__*` from Azure (r1 criterion 10 unclosed). | r1 lessons | Schedule the migration (resolve env per-request via registry lookup) as a tracked carry-over; not on the customer-provisioning critical path but must not be forgotten. |
| R6 | Doc drift: `auth-azure-resources.md` names retired App Service `spe-api-dev-67e2xz` (actual: `spaarke-bff-dev`/`rg-spaarke-dev`). | r1 lessons | Fix during doc consolidation; handlers must read names from config/registry, never hardcode. |
| R7 | "Validated but not wired" class of defect (r1 FR-11: license parsed, never applied). | r1 lessons | Every handler's acceptance asserts the value **reaches its consumer**, not just that it was computed. Validation gate (H13) checks effects, not intentions. |
| R8 | CORS localhost leakage, ChatModelName, max-upload-size, import order, canvas-app deps (5 of the 13). | ENV-GUIDE §13 | Fold each into the relevant handler's post-conditions + the validation script. |

---

## 6. Scope boundaries (this program)

**In scope**: L1 handler catalog + L2 control-plane API/MCP design + L3 operator skill design; ProvisioningRun data model; managed-solution packaging; identity/subscription parameter model; consolidation of the 3 doc generations; the verified-gate model.

**Out of scope** (named to prevent creep):
- Demo/customer **data-seeding tooling** beyond config + minimal starter data — `spaarke-data-cli` is a separate repo/project (H12 stays thin here).
- **Multi-tenant architecture** changes (`spe-multi-tenant-architecture-r1`).
- Changes to the **ongoing release** process (`/deploy-new-release` consumed as-is).
- **Disaster recovery / backup** automation.
- The **L3 MDA fleet dashboard + Spaarke Assistant** front ends (design-acknowledged, built later; only the operator skill is near-term).

**Carry-overs (tracked, not on critical path)**: R5 DemoExpirationService migration; R6 doc drift; r1 live-provisioning sign-off (criteria 5/8/9/11) — folded into the eventual E2E dry run.

---

## 7. Open questions for the Phase 1 ADR (must be resolved before design specs)

| Q | Question | Why it blocks design |
|---|----------|----------------------|
| **Q1** | **Control-plane placement & the fate of `platform.bicep`.** D3 forbids shared resources between customers, so the control-plane service is a separate Spaarke-internal deployable. Is it (a) a new standalone service/Container App, (b) a module in an existing internal service, or (c) something else? And do any "shared platform" resources legitimately remain (control plane itself, demo/registration surfaces)? Touches CLAUDE.md §10 BFF-governance / placement-justification. | Determines where Layer 2 lives and the §10 placement decision. |
| **Q2** | **ProvisioningRun store.** Dataverse `sprk_provisioningrun` entity vs control-plane-owned store (Cosmos serverless per D7) vs ADR-017 job-status extended? Trade-off: Dataverse gives MDA-dashboard reads for free (L3); Cosmos gives the control plane an independent system of record not coupled to any customer Dataverse. | Determines the data-model spec and the L2 API's persistence. |
| **Q3** | **Headless Dataverse application-user creation (H10).** Can the BAP/Admin API or `pac admin` create + role-assign the app user without PPAC UI? If not, H10 is a manual-by-design gate. | Determines whether the pipeline is fully unattended or has one mandatory human gate. |
| **Q4** | **Decommission scope.** Is registry-aware `Decommission-Customer.ps1` (teardown handlers) in this program, or deferred to r2? | Sets the handler-catalog boundary. |
| **Q5** | **Environment profiles.** Does the orchestrator need named profiles (customer-hosted / customer-owned / demo / trial) selecting bicep stack + gate set, or is it pure parameters (subscription target + identity preset + tenant)? Brief leans pure-parameters; confirm. | Determines the parameter model spec. |
| **Q6** | **MCP server runtime & auth.** How does the `spaarke-provisioning` MCP server authenticate Claude Code as an authorized internal client (D9)? Where does it run relative to the control-plane API (same process / sidecar)? | Determines the L2 + L3 boundary and security model. |

---

## 8. Readiness assessment

- **Foundation strength**: high. The ADR-004 job contract + ~15 production handlers + idempotent `Provision-Customer.ps1` + per-customer Bicep mean the **invariant engine (L1) is largely a port, not a greenfield build**. The locked decisions map cleanly onto existing primitives (D11→ADR-004, D5→FR-11, D4→ARM verification).
- **Biggest unknowns**: control-plane placement (Q1), run-store choice (Q2), and the one genuinely manual step (Q3, app user). All three are ADR-level decisions, correctly deferred to Phase 1.
- **Recommendation**: **proceed to Phase 1** (draft ADR resolving Q1–Q6, then the four design specs: handler catalog, L2 API + MCP surface, data model, parameter model) once the owner has reviewed this report and answered Q1–Q6 (or delegated them to the ADR with a default recommendation each).

---

*End of Phase 0 discovery report. Phase 1 is gated on review of this document.*
