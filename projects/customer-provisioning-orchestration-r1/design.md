# Customer Provisioning & Deployment Orchestration — Design Specification

> **Status**: Draft — pending owner review
> **Created**: 2026-06-15
> **Author**: Ralph Schroeder / Claude Code
> **Project**: customer-provisioning-orchestration-r1
> **Supersedes**: `projects/spaarke-environment-factory-r1/design.md`
> **Predecessors**: `spaarke-environment-provisioning-app` (r1, complete PR #390), Phase 0 discovery report (`discovery/phase-0-discovery-report.md`)

---

## 1. Executive Summary

Build the single, systematic process for standing up a new Spaarke customer environment and deploying the platform into it. One orchestrated pipeline, driven by a control-plane API with MCP tooling, backed by idempotent deterministic handlers, all state-tracked against the `sprk_dataverseenvironment` registry.

When a paying customer is approved, the pipeline provisions a dedicated Dataverse environment, deploys the full managed solution, stands up per-customer isolated Azure resources (OpenAI, AI Search, Document Intelligence, Service Bus, Redis, Key Vault, App Insights, BFF App Service), seeds configuration, wires post-deploy integrations, and verifies the result. The same package deploys into a customer's own tenant (Model 2) with target tenant as the only meaningful variable.

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

These are inputs, not proposals. The design conforms to them. Full rationale in `discovery/phase-0-discovery-report.md` section 1.

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

---

## 4. Three-Layer Architecture

### 4.1 Layer 1 — Deterministic Handlers

Provisioning steps implemented as idempotent handlers. Each handler is a self-contained, coarse-grained operation (deploy infrastructure, import solutions, deploy BFF) that fits the ADR-004 job contract individually.

**Existing substrate**: 13 production `IJobHandler` implementations prove the pattern at scale across RAG indexing, invoice processing, email analysis, attachment classification, and spend snapshots. Three-level idempotency is proven: Service Bus `MessageId` deduplication, Redis-backed `IdempotencyService` check/lock, Dataverse alternate keys/upserts.

**Handler catalog** (derived from `Provision-Customer.ps1` 13 steps + locked decisions):

| # | Handler | Source logic | Gate | Idempotency key |
|---|---------|-------------|------|-----------------|
| H0 | Preflight / validate inputs | Step 1 + runbook checklist | -- | `preflight-{customerId}` |
| H1 | Subscription readiness | NEW (D4) — ARM verification | **Lighthouse delegation** (CustomerOwned) | `subready-{customerId}` |
| H2 | Resource group + infra (per-customer Bicep) | Steps 2-3, `customer.bicep` + modules | -- | `infra-{customerId}-v{n}` |
| H3 | Entra app registrations (per-customer) | `Register-EntraAppRegistrations.ps1` (D2) | **Admin consent granted** (Graph) | `appreg-{customerId}` |
| H4 | Key Vault secrets population | Step 4 | -- | `kv-{customerId}-v{n}` |
| H5 | Dataverse environment creation | Steps 5-6 (Power Platform Admin API) | -- | `dvenv-{customerId}` |
| H6 | Solution export/fix (managed) + import | Export (NEW, D1) + `Deploy-DataverseSolutions.ps1` | -- | `solimport-{customerId}-v{ver}` |
| H7 | Dataverse environment variables (7) | Step 8 | -- | `envvars-{customerId}-v{n}` |
| H8 | SPE container type + root container | `Create-NewContainerType.ps1` | (async token propagation wait) | `spe-{customerId}` |
| H9 | BFF deploy + app settings | `Deploy-BffApi.ps1` + `auth-deployment-setup.md` | -- | `bff-{customerId}-v{buildId}` |
| H10 | Dataverse application user (UAMI) | Manual today (PPAC) — see Q3 | -- | `appuser-{customerId}` |
| H11 | User provisioning (identity preset) | r1 registration flow (D6) | **B2B consent** (B2BGuest only) | `users-{customerId}` |
| H12 | Config + starter-data seeding | Per-module seed scripts (scope-limited) | -- | `seed-{customerId}-v{n}` |
| H13 | Validation gate | `Validate-DeployedEnvironment.ps1` (exit 0/1) | **Validation passed -> registry Ready** | `validate-{customerId}-v{n}` |
| H14 | Post-deploy integration wiring | NEW | -- | `integrations-{customerId}` |

### 4.2 Layer 2 — Control-Plane API + MCP Server

The orchestration layer that sequences handlers, manages run state, enforces gates, and exposes tooling to front ends.

**MCP tools** (indicative):

| Tool | Purpose |
|------|---------|
| `create_provisioning_run` | Initialize a run against an environment record |
| `run_preflight` | Execute H0, return parameter validation results |
| `get_run_status` | Return current phase, completed phases, gate states |
| `advance_gate` | Operator marks a manual gate as cleared (e.g., H10 app user) |
| `resume_run` | Resume a failed run from the failure point |
| `get_phase_logs` | Return logs/output for a specific phase |
| `cancel_run` | Cancel an in-progress run |

**Placement**: Separate Spaarke-internal service (D3, D8). Not in the BFF — the BFF is per-customer; the control plane manages the fleet. See Q1 for final placement decision.

### 4.3 Layer 3 — Swappable Front Ends

| Front end | Timeline | Character |
|-----------|----------|-----------|
| Claude Code operator skill (`/provision-environment`) | This project | Interactive, modeled on `/deploy-new-release` |
| MDA fleet dashboard | Future | Thin read-only view over `sprk_dataverseenvironment` + `sprk_provisioningrun` |
| Spaarke Assistant integration | Future | Natural-language provisioning via the same MCP tools |

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

BFF already at 269 registrations (17x the 15-line limit, acknowledged violation). Provisioning handlers should register in the **control-plane service**, not the BFF — aligns with D3/D8 and keeps BFF impact at zero.

### 5.3 ADR-017 — Status Granularity

Per-handler job status (ADR-017) governs individual handler outcomes. The **ProvisioningRun record** (Q2) provides multi-phase orchestration state. No ADR change needed — different concerns, different stores.

### 5.4 What You'd Do Differently Without the ADRs

Only one thing: you might use Azure Durable Functions or Temporal for the control plane instead of building a custom state machine. Everything else (no plugins, Minimal API, ProblemDetails) correctly guides the design. The state-machine approach is more work but avoids infrastructure sprawl, and `Provision-Customer.ps1`'s state-file pattern already proves it works.

---

## 6. Data Model

### 6.1 `sprk_dataverseenvironment` — Fleet Inventory (extend existing)

The r1 entity has 16 columns deployed. Extend with provisioning infrastructure fields:

| Schema Name | Type | Purpose |
|---|---|---|
| `sprk_azuresubscriptionid` | Text(100) | Azure subscription hosting this environment |
| `sprk_resourcegroupname` | Text(200) | Resource group |
| `sprk_appservicename` | Text(200) | BFF App Service |
| `sprk_keyvaultname` | Text(200) | Key Vault |
| `sprk_containertypeid` | Text(100) | SPE container type |
| `sprk_provisionedon` | DateTime | When validation first passed |

### 6.2 ProvisioningRun — Execution State (new)

One execution of the pipeline against a target. Multiple runs per environment over time (initial provision, re-provision, repair).

| Field | Type | Purpose |
|---|---|---|
| RunId | Guid | Unique run identifier |
| EnvironmentId | Lookup -> `sprk_dataverseenvironment` | Target environment |
| Status | Choice | NotStarted, Running, WaitingOnGate, Completed, Failed, Cancelled |
| CurrentPhase | Integer | Current handler (0-14) |
| CompletedPhases | JSON | Array of completed phase numbers with timestamps |
| GateStates | JSON | Per-gate verification results |
| Parameters | JSON | Run parameters (customerId, tenantId, identityPreset, subscriptionTarget, etc.) |
| InterStepState | JSON | Data passed between handlers (entraUserId, systemUserId, etc.) |
| AttemptCount | Integer | Number of resume attempts |
| CreatedAt | DateTime | Run creation |
| CompletedAt | DateTime | Run completion (success or final failure) |
| ErrorDetail | Text | Last error message |

**Store location**: Q2 — Dataverse `sprk_provisioningrun` entity (MDA dashboard reads for free) vs Cosmos serverless (control-plane-independent). `cosmos-db.bicep` module already exists if Cosmos is chosen.

---

## 7. Existing Asset Disposition

Verified against the codebase 2026-06-15. Legend: **PORT** = logic feeds a handler; **REUSE** = consumed as-is; **REBUILD** = concept kept, form changes; **NEW** = does not exist.

### 7.1 Scripts and Orchestration

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `Provision-Customer.ps1` | `scripts/` | **PORT** | 13 steps -> handler catalog. State-file resume -> ProvisioningRun record. |
| `Decommission-Customer.ps1` | `scripts/` | **PORT** (Q4 scope) | Teardown handlers mirror provisioning. |
| `Deploy-DataverseSolutions.ps1` | `scripts/` | **REUSE** | Called by H6. Confirm managed-capable (D1). |
| `Deploy-BffApi.ps1` | `scripts/` | **REUSE** | Called by H9. |
| `Validate-DeployedEnvironment.ps1` | `scripts/` | **REUSE** | Called by H13. Exit code -> gate state. |
| `Test-Deployment.ps1` | `scripts/` | **REUSE** | Smoke-test handler. |
| `Register-EntraAppRegistrations.ps1` | `scripts/` | **PORT** | Basis for H3. Needs full idempotency (creation + 11 permission grants). |
| `Create-NewContainerType.ps1` | `scripts/` | **PORT** | Basis for H8. Must encode westus-billing + token-propagation-wait workarounds. |
| `Deploy-Release.ps1` + `/deploy-new-release` | `scripts/`, `.claude/skills/` | **REUSE as-is** | Out of scope. Reference model for L3 skill UX. |

### 7.2 Infrastructure-as-Code

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `customer.bicep` | `infrastructure/bicep/` | **REUSE + EXTEND** | Already per-customer. Extend for dedicated OpenAI/Search/DocIntel/AppInsights (D3, D7). |
| `platform.bicep` | `infrastructure/bicep/` | **REBUILD intent** | "Shared platform" conflicts with D3. May persist only for control plane + demo surfaces. Q1. |
| 18 Bicep modules | `infrastructure/bicep/modules/` | **REUSE** | Composable building blocks. Cosmos serverless + OpenAI standard already expressible (D7). |
| model1/model2 stacks | `infrastructure/bicep/stacks/` | **ASSESS** | May encode one-package-two-targets (D2). Reconcile in Phase 1. |

### 7.3 BFF Job Handler Ecosystem

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `IJobHandler` + ADR-004 | `Services/Jobs/` | **REUSE** | Provisioning handlers implement this contract. |
| 13 production handlers | `Services/Jobs/Handlers/`, `Services/Ai/Jobs/` | **REFERENCE** | Pattern exemplars for handler structure, idempotency, telemetry. |
| `JobSubmissionService` | `Services/Jobs/` | **ASSESS** | Enqueue mechanism. Provisioning may need a dedicated queue or the control plane enqueues directly. |
| `IdempotencyService` (Redis) | `Services/Jobs/` | **REUSE** | Three-level idempotency proven. |
| `BatchJobStatusStore` | `Services/Jobs/` | **ASSESS** | Per-job progress. ProvisioningRun record provides richer multi-phase state. |

### 7.4 Registration/Provisioning Services

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `DemoProvisioningService` (9-step) | `Services/Registration/` | **PORT** | User provisioning logic -> H11. |
| `RegistrationDataverseService` | `Services/Registration/` | **REUSE** | Cross-env token cache + multi-URL ops directly applicable to handlers. |
| `DataverseEnvironmentService` | `Services/Registration/` | **REUSE** | Reads registry records. No caching per NFR-01. |
| `GraphUserService` | `Services/Registration/` | **REUSE** | User creation, UPN generation, license assignment (D5). |
| `DemoExpirationService` | `Services/Registration/` | **CARRY-OVER (R5)** | Must migrate off `[Obsolete]` options. Not critical path. |

### 7.5 Documentation (3 generations)

| Asset | Path | Disposition |
|---|---|---|
| `ENVIRONMENT-DEPLOYMENT-GUIDE.md` (14 sections, 13 known issues) | `docs/guides/` | **MINE then SUPERSEDE** — known issues -> risk register; manual steps -> handler requirements. |
| `CUSTOMER-ONBOARDING-RUNBOOK.md` (9 sections) | `docs/guides/` | **MINE** — pre-checklist -> preflight inputs; escalation -> failure-mode design. |
| `auth-deployment-setup.md` (auth v2, 21 MUSTs) | `docs/guides/` | **REUSE** — app-settings + UAMI + Dataverse-app-user -> BFF-config handler contract. |

### 7.6 `spaarke-data-cli` (separate repo)

**Location**: `C:\code_files\spaarke-data-cli`
**Status**: Pre-alpha scaffolding (269 lines TypeScript, 2 commits, zero implementation).
**Relevance**: H12 (seeding) maps to this CLI's `load` command long-term. The `onboard` command (Phase 3) is the eventual customer data import pipeline.
**Design decision**: H12 stays thin in this project (config + minimal starter data via existing PowerShell seed scripts). The CLI's value is medium-term (demo quality, customer onboarding) and is a separate project dependency.

---

## 8. Risk Register

Absorbed from the 13 known deployment-guide issues + r1 carry-overs. Verified against codebase 2026-06-15.

| ID | Risk / known issue | Source | Design must... |
|---|---|---|---|
| R1 | SPE container-type creation requires SPO Management Shell (not Graph), billing must use `westus`, and 10-30 min Graph token-propagation wait. | ENV-GUIDE issue 8, 9, 13 | Make H8 idempotent + async-wait-aware; encode region + wait workarounds; expose as verified gate, not fixed sleep. |
| R2 | Dataverse application user creation is PPAC-UI-only (no documented API/CLI). | ENV-GUIDE issue 11 | Resolve H10: confirm BAP/Admin API can create headlessly, else mark as manual-by-design gate. Q3. |
| R3 | Solution export/fix pipeline is 8 manual sed-style steps; managed-vs-unmanaged changes it (D1). | ENV-GUIDE section 6 | H6 scripts export->fix->pack-managed->verify; no manual edits. |
| R4 | Entra app reg = 11 permission GUIDs granted by hand; no recovery script. | ENV-GUIDE section 4 | H3 scripts grants idempotently; admin-consent is a verified gate (D10). |
| R5 | `DemoExpirationService` still binds `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment`; blocks deleting Azure config. | r1 lessons | Carry-over: migrate to registry lookup. Not critical path but tracked. |
| R6 | Doc drift: `auth-azure-resources.md` names retired `spe-api-dev-67e2xz`; `spaarke-data-cli/environments.yaml` has same stale URL. | r1 lessons + data-cli review | Fix during doc consolidation; handlers read names from config/registry, never hardcode. |
| R7 | "Validated but not wired" defect class (r1 FR-11: license parsed, never applied). | r1 lessons | Every handler's acceptance asserts value reaches its consumer. H13 checks effects, not intentions. |
| R8 | CORS localhost leakage, missing ChatModelName, max-upload-size < PCF bundle, solution import order, canvas-app deps. | ENV-GUIDE issues 1-7, 10 | Fold each into relevant handler's post-conditions + validation script. |

---

## 9. Scope

### In Scope

1. **L1 handler catalog** — 15 handlers (H0-H14) implementing the provisioning pipeline as idempotent `IJobHandler` implementations
2. **L2 control-plane API + MCP server** — run lifecycle, gate management, state persistence
3. **L3 operator skill** — `/provision-environment` Claude Code skill modeled on `/deploy-new-release`
4. **ProvisioningRun data model** — per-execution state tracking with phase granularity
5. **Registry extension** — 6 new columns on `sprk_dataverseenvironment` for infrastructure references
6. **Gap automation scripts** — Entra app registration (H3), SPE container type (H8), solution export/fix managed pipeline (H6)
7. **Managed-solution packaging** — scripted export/fix/pack-managed/verify pipeline (D1)
8. **Parameter model** — customerId, tenantId, identityPreset, subscriptionTarget, and environment-specific config
9. **Documentation consolidation** — one canonical procedure superseding the 3-generation documentation
10. **E2E dry run** — stand up one brand-new environment using only the new pipeline

### Out of Scope

- Demo/customer **data-seeding tooling** beyond config + minimal starter data (`spaarke-data-cli` is separate; H12 stays thin)
- **Multi-tenant architecture** changes (`spe-multi-tenant-architecture-r1`)
- Changes to the **ongoing release process** (`/deploy-new-release` consumed as-is)
- **Disaster recovery / backup** automation
- **L3 MDA fleet dashboard + Spaarke Assistant** front ends (design-acknowledged, built later)
- **CI/CD workflow changes** (existing workflows consumed as-is; handlers call underlying scripts directly)

### Carry-Overs (tracked, not critical path)

- R5: `DemoExpirationService` migration off `[Obsolete]` options -> registry lookup
- R6: Doc drift fixes (`auth-azure-resources.md`, `spaarke-data-cli/environments.yaml`)
- r1 live-provisioning sign-off (criteria 5/8/9/11) folded into E2E dry run

---

## 10. Phasing

| Phase | Content | Depends on | Notes |
|---|---|---|---|
| A | Canonical procedure doc + Gen-1 guide supersession + doc-drift fixes | -- | Parallel with B |
| B | Gap automation scripts (Entra apps, SPE container type, solution export/fix managed) — independently testable | -- | Parallel with A |
| C | Registry schema extension + ProvisioningRun data model + control-plane orchestrator integrating handlers | A, B | Core build phase |
| D | `/provision-environment` operator skill + MCP server | C | L3 |
| E | `DemoExpirationService` migration + Azure legacy-config deletion + verification | -- | Parallel; BFF task, FULL rigor |
| F | E2E dry run: new environment end-to-end + r1 live sign-off items + wrap-up | C, D, E | Acceptance |

---

## 11. Success Criteria

1. One procedure doc covers all 15 provisioning phases with a single automation entry point or explicit manual-by-design marking per phase
2. Each handler (H0-H14) is idempotent, independently testable, and reports its outcome to the run record
3. The control plane sequences handlers, manages gates, and supports resume-from-failure
4. Entra app registration, SPE container type, and solution export/fix run unattended and idempotently
5. A brand-new environment reaches `Setup Status = Ready` via the new pipeline; `Validate-DeployedEnvironment.ps1` exits 0
6. `DemoProvisioning__Environments__*` and `__DefaultEnvironment` deleted from Azure; expiration flow verified working
7. `/provision-environment` skill executes the full flow with confirmation gates and produces a handoff report
8. `sprk_provisioningrun` records are queryable for fleet status (how many environments, in what state)

---

## 12. Open Questions (must be resolved before implementation)

These questions were identified in the Phase 0 discovery report and confirmed unresolved during the 2026-06-15 resource review. Each blocks specific design specs.

| Q | Question | Context from review | Blocks |
|---|----------|-------------------|--------|
| **Q1** | **Control-plane placement & fate of `platform.bicep`.** Where does L2 live? (a) New standalone service/Container App, (b) module in existing internal service, (c) other? Do any "shared platform" resources legitimately remain? | `platform.bicep` deploys shared App Service + OpenAI + AI Search + Doc Intelligence. D3 says no shared resources between customers. Control plane itself + demo/registration surfaces may be the only legitimate shared resources. BFF Hygiene (CLAUDE.md section 10) requires Placement Justification. | L2 design, deployment topology |
| **Q2** | **ProvisioningRun store.** Dataverse `sprk_provisioningrun` entity vs Cosmos serverless vs ADR-017 extension? | `cosmos-db.bicep` module exists and is ready. Dataverse gives MDA dashboard reads for free. Cosmos gives the control plane an independent system of record. ADR-017 status schema is too simple for multi-phase runs (confirmed in review). | Data model spec, L2 persistence |
| **Q3** | **Headless Dataverse application-user creation (H10).** Can BAP/Admin API or `pac admin` do it without PPAC UI? | Gen 1 guide issue 11 confirms this is PPAC-UI-only today. If no headless path exists, H10 is a manual-by-design gate the operator clears via `advance_gate`. | Handler catalog completeness |
| **Q4** | **Decommission scope.** Registry-aware `Decommission-Customer.ps1` (teardown handlers) in this project or deferred to r2? | `Decommission-Customer.ps1` is prod-ready with DryRun/Force. PORT disposition confirmed. | Handler catalog boundary |
| **Q5** | **Environment profiles vs pure parameters.** Named profiles (customer-hosted / customer-owned / demo / trial) selecting bicep stack + gate set, or pure parameters? | `model1/model2` stacks in `infrastructure/bicep/stacks/` may already encode the two-target idea (D2). Suggest pure parameters with `subscriptionTarget` + `identityPreset` as the two key discriminators. | Parameter model spec |
| **Q6** | **MCP server runtime & auth.** How does `spaarke-provisioning` MCP server authenticate Claude Code (D9)? Same process as control-plane API or sidecar? | No existing MCP server pattern in the codebase to reference. `/deploy-new-release` skill is the closest UX analog but runs as a Claude Code skill, not MCP. | L2 + L3 boundary, security model |

---

## 13. Placement Justification (CLAUDE.md section 10)

- **New scripts + skill + procedure doc**: `scripts/`, `.claude/skills/`, `docs/procedures/` — no BFF impact.
- **Provisioning handlers**: Register in the **control-plane service**, not the BFF. The control plane is Spaarke-internal fleet management (D3, D8); the BFF is per-customer. Zero BFF DI impact.
- **Only BFF change**: `DemoExpirationService` migration (R5 carry-over) — modifies an existing registered service to use `DataverseEnvironmentService`. No new endpoints, packages, or DI registrations. Expected publish-size delta: ~0.
- **Registry schema extension**: Dataverse-only (6 new columns on existing entity).

---

*End of design specification. Next step: owner review of this document + answers to Q1-Q6, then `/design-to-spec` -> `/project-pipeline`.*
