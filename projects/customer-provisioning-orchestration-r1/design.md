# Customer Provisioning & Deployment Orchestration — Design Specification

> **Status**: **Draft v3.6 — task 128b Redis Model 1/Model 2 reconciliation (E2): Redis reinstated as per-customer for Model 2 (dedicated) only, reversing v3.2's blanket-shared decision for that case; Model 1 (shared) unchanged. Owner-confirmed 2026-08-19.** v3.5 content otherwise carried forward.
> **Created**: 2026-06-15
> **Revised**:
> - 2026-06-16 (feedback round 1: resource inventory, identity spec, config capture, Q1–Q6 resolved)
> - 2026-08-12 (v3: D3 rewritten for two tiers, TF Power Platform provider adoption, H12 promoted to first-class config-seed manifest, silent-failure trap catalog, Cosmos DB provisioning added, SPE confidential-client fix, resolved v2 open items B1–B3/I1–I3/I5–I6)
> - 2026-08-12 (v3.1: D20 fail-fast config + Graph role constants added as PENDING per r3 handoff; sourced pricing research; D3 rewritten in place; §3A reframed as rationale not amendment)
> - 2026-08-15 (v3.2: r3 handoff resolved — D20 LANDED (tasks 060/061/062/017); Fable-adversarial review corrections (H-1 Deploy-AllIndexes path, H-2 AI Search catalog, H-3 UAMI vs system-assigned reality); dropped vestigial Dataverse S2S app-reg (r3 task 060); added Phase G naming compliance + Phase H #1 KV federation remediation per r3 handoff §4a/§4b; added §4C rollback semantics + §4.2 handler execution model + Model 1/2 handler-behavior differences + H0 quota preflight; Redis moved per Q-E FR-12; TF adoption deferred to first-customer engagement; dev-only baseline with new trial environment for Phase F E2E)
> - 2026-08-16 (v3.3: owner-review round Q1–Q7 addressed. **New §4.3a Claude Code Operator Toolchain** (Q4 — 15 tools + auth flow + prereqs + skill definition + fallback matrix); **new §4D Tenant Isolation Invariants** (Q6 — 5 binding invariants + code-fix Register-EntraAppRegistrations.ps1:63 hardcoded tenant removed); **new §9A Consolidated Identity + Config Surface** (Q7 — one-page reference); **new §11.1a Solutions Reconciliation** (Q2 — authoritative 8-solutions from Deploy-DataverseSolutions.ps1 vs 36 in src/solutions/); **new §14A Upgrade Model** (Q3 — U1/U2/U3 classes + handler upgrade-mode semantics + version-compatibility matrix + U-CB breaking-change classes + drift detection); **§5.4 expanded** into proper trade-off table (Q1 custom state-machine vs Durable Functions vs Temporal); **Q5 Graph/SPE 2026-08 spike** confirmed v3.2 patterns still current — added R22 (ExchangePolicy→RBAC-for-Apps migration watch), R23 (MI-as-FIC opportunity), H8 SPE-privilege footnote)
> - 2026-08-18 (v3.4: Wave A design studies integrated per DS-6 amendment text. **§4.2 restructured** — execution model corrected from "BFF's IJobHandler infrastructure" (contradicted the design's own D8/D12 + MUST rules and left the dispatcher unowned — the root cause of Phase F shipping without E2E) to L2-owned Option D: new §4.2a Runtime & Deployment Topology (stock App Service + EXO sidecar sitecontainer per DS-1b), new §4.2b Dispatcher & Handler Resolution (ServiceBusSessionProcessor, SessionId=CustomerId, MaxConcurrentCallsPerSession=1, keyed DI by HandlerId per DS-2/DS-2b); **new §4.1b** handler runtime classification (12 Class-A pure-.NET ports + H14 Class-C + 6 in-process); **H9 re-scoped** to CI-artifact deploy; **§4C** retry envelope gains `attempt`; **§6.2** serialization contract (Newtonsoft StringEnumConverter on all run-doc enums per C4.5); **new §9.6** L2 control-plane identity — Path X UAMI-as-Dataverse-App-User (DS-8); §9A row 15; §11.2/§11.3 dispositions updated; §14 Phase C'' wave plan; §14A L2/sidecar upgrade surface; §15 SC updates + SC 23; §16 v3.4 resolutions B6–B11.)
> - 2026-08-19 (v3.5: `spaarke-auth-v4-dataverse-MI` coordination — MI-FIC adoption for the customer BFF's OBO credential per ADR-028 Amendment A4 + Exception E-3. Reconciles r1's own Q5-spike R23 framing (this project's DS-8 Path Z, §9.6) against auth-v4's corrected MI-as-issuer-vs-MI-as-recipient cap analysis. **D2 + §9.1 tenancy note corrected** — split the Model 1 vs Model 2 app-registration shape (Model 1: one shared multitenant app-reg, no per-customer creation; Model 2: per-customer app-reg + FIC) and fixed the §9.1 sentence that read as licensing a Spaarke-owned app-reg with customer-tenant compute (ruled out 2026-08-18); **§4.1 H3 row split** into Model 1 (no-op app-reg, consent-callback captures trust) / Model 2 (per-customer app-reg + FIC) branches; **new invariant I6** (Model 1 only, §4D + spec FR-40); **§12 R23 CLOSED** with corrected cap analysis; **§9.6 cross-reference** distinguishing Path X (L2's own Dataverse credential, unaffected) from auth-v4's BFF-OBO migration (separate concern, FR-39). Response: `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md`.)
> - 2026-08-19 (v3.6: task 128b Redis Model 1/Model 2 reconciliation (E2) — `customer.bicep` completion task 128b wired `modules/doc-intelligence.bicep` + `modules/monitoring.bicep` + `modules/redis.bicep` (Document Intelligence + App Insights/Log Analytics + Redis). The Redis wiring reverses v3.2's blanket "Redis is per-environment, not per-customer" decision **for Model 2 (dedicated) only** — `customer.bicep` is confirmed (task 129's own background) to be the sole template deployed for the Model2Dedicated branch, where env=customer 1:1, so "per-environment" and "per-customer" collapse to the same unit for that template. Model 1 (shared/trial) is UNCHANGED — Redis remains per-env-shared via `scripts/Deploy-RedisCache.ps1`; Model 1 has no code path through `customer.bicep`. **§7.1 naming table**: Redis row reinstated (Model 2 only), `sprk-{customerId}-{env}-redis`. **§7.2 Resource Catalog row 6**: unstruck for Model 2 (struck row retained as historical marker, annotated Model 1 only). **§7.2 disposition table**: Redis row split from "🔵 shared \| shared" to "🔵 shared (Model 1) \| 🟢 dedicated per-customer (Model 2)". **§7.6 Deployment Order step 8**: unstruck for Model 2 (struck for Model 1). **§7.7**: `redis-connection-string` KV secret restored for Model 2 (per-customer, task 129 territory to wire); Model 1 continues to use the platform-KV per-env Redis reference, unaffected. Owner-confirmed 2026-08-19 via AskUserQuestion during task 128's authoring; Path B (spec/design amendment) per root CLAUDE.md §6.5, folded into task 128b's step 8 per owner direction. **Note**: task 128b's own POML cited "v3.3" as this amendment's target version; that number was already taken by the 2026-08-16 owner-review-round entry below by the time 128b executed (this file had independently advanced to v3.5 the same day via the auth-v4 coordination entry above) — v3.6 is used instead as the correct next-sequential version, to avoid a duplicate/misleading marker.)
> **Author**: Ralph Schroeder / Claude Code
> **Project**: customer-provisioning-orchestration-r1
> **Supersedes**: `projects/spaarke-environment-factory-r1/design.md`
> **Predecessors**: `spaarke-environment-provisioning-app` (r1, complete PR #390), Phase 0 discovery report (`discovery/phase-0-discovery-report.md`)
> **Companion docs (v3 authoritative supplements)**:
> - [`PROJECT-UPDATE-2026-08-12.md`](PROJECT-UPDATE-2026-08-12.md) — 2026-08-12 six-workstream assessment + design-refresh rationale + fast-follow list
> - [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) — machine-hardened bill-of-materials for one customer environment (386 solution components, 87+ entities, 33-PCF/7-in-use gap, Azure stamp, config/seed layer)
> - [`notes/pricing-research-2026-08-12.md`](notes/pricing-research-2026-08-12.md) — sourced Azure + M365 list pricing (Aug 2026) + Model 2 baseline + Model 1 shareable-vs-dedicated segregation and shared-platform-floor breakdown
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
| D2 | **One deployment package, two targets.** Variable = target tenant (Spaarke vs customer). ~~Per-customer app registrations in both models.~~ **v3.5 corrected (2026-08-19, per auth-v4 coordination)**: Model 1 uses ONE shared multitenant BFF app registration (not per-customer); Model 2 retains per-customer app registrations for tenant isolation. See §9.1 tenancy note + FR-39. | Tenant is a run parameter, not a code fork. |
| D3 (v3 rewritten, Model 1 UAMI wording corrected 2026-08-26 per Q8 owner disposition / task 205d A41) | **Two deployment tiers, isolation posture per tier**:<br>• **Model 2 (dedicated stamp, default for regulated/enterprise)**: no shared resources between customers. One BFF per customer env. Dedicated per-customer: OpenAI, AI Search, Doc Intelligence, Service Bus, Redis, Key Vault, App Insights, Cosmos DB.<br>• **Model 1 (shared trial/SMB tier)**: fixed-floor resources shared across trials with per-tenant logical isolation — App Service Plan, Azure OpenAI (metered per D19), AI Search (per-tenant `tenantId` filter on every query); everything else remains per-customer dedicated (Dataverse, SPE, Key Vault, Storage, Entra app config). **In Model 1, the BFF Managed Identity is a SINGLE shared `sprk-{env}-shared-bff-uami` per environment (NOT per-customer)** — coordinated per auth-v4 MI-FIC contract (Amendment A4); per-customer isolation is code-level via invariant I6 (see FR-40 + task 130) and slot-persistence-safe per the task-029 slot-swap fix (T5 structural fix binding the shared UAMI to both slots so slot-swap does not rotate downstream RBAC / Dataverse App User / Graph app-role grants). Model 2 retains a per-stamp UAMI (`mi-spaarke-{customerId}-{env}`).<br>Rationale + economic analysis: §3A. | Control plane is a separate Spaarke-internal service. Bicep composition selects the stack per tier: `model2-full.bicep` (dedicated) or `model1-shared.bicep` (shared platform + per-customer dedicated). |
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
| D20 | **(v3 landed, v3.2 confirmed 2026-08-15) Fail-fast configuration validation + code-constant Graph role enforcement — LANDED via r3.** r3 shipped: task 061 (`.ValidateDataAnnotations().ValidateOnStart()` on 24 Tier-1 customer-critical `IOptions<T>` classes; 17 Tier-2 kill-switch-gated exemption list authored; test coverage green — see `projects/code-quality-and-assurance-r3/notes/task-061-config-validation-classification.md`), task 062 (`src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs` — 14 role entries; **11 of 14 `AppRoleId` GUIDs pending live enumeration per class remarks — r1 H10 completion obligation**), task 060 (vestigial Dataverse S2S app-reg dropped — zero code consumers), task 017 (KV federation assessment complete — r1 owns remediation per Phase G + H below). r3 forcing-functions gate (task 042 ArchTests) enforces validation-on-start + no-secret-Dataverse across future BFF PRs; nightly Graph-app-role parity check queued behind CI-workflow wiring coordination with `ci-cd-unit-test-remediation-r1`. | r1's Phase B + Phase E no longer carry conditional D20 absorption. r1's remaining D20-adjacent work: (a) H10 grants the 14 roles enumerated in `GraphAppRoles.cs` onto UAMI SP + verifies parity against the constant; (b) H10 escalation: complete the 10 missing GUIDs via `az` enumeration of the Graph resource SP; (c) H4 gets lighter — silent-fail traps T1/T2/T3 now fail deploy verification (`/health` probe) via r3-shipped ValidateOnStart instead of first-user runtime. Legacy H4/H10 verification queries kept as belt-and-suspenders safety net until r3 CI wiring is live. |

---

## 3A. D3 Two-Tier Rationale (added v3, 2026-08-12)

**Why D3 (v3) has two tiers rather than one dedicated model.** The v2 formulation of D3 was "no shared resources between customers, ever." That's correct for regulated legal customers requiring physical isolation, and it dissolves cost-allocation (Azure Cost Management + tags = native per-customer bill = zero metering infra + honest usage-passthrough pricing). But the 2026-08-12 assessment identified three resources that carry a **fixed monthly floor regardless of usage** — App Service Plan, Azure OpenAI (provisioned TPM), Azure AI Search (fixed tier) — that make a strict per-customer stamp uneconomic for trial/SMB prospects. Rather than force every prospect through the dedicated-stamp cost floor (or refuse to serve them), D3 (v3) explicitly covers both:

- **Model 2 (dedicated stamp)** — default for regulated/enterprise. Honest usage-passthrough pricing; regulated-legal-grade isolation; Azure Cost Management + tags for per-customer billing without additional metering infra.
- **Model 1 (shared trial/SMB tier)** — for prospects where the fixed floor is uneconomic. Shares the three fixed-floor resources; keeps everything else per-customer dedicated; adds per-tenant token metering (D19) so allocation is fair.

**Three supporting decisions that pair with D3 (v3):**

| # | Decision | Purpose |
|---|---|---|
| A1 (Bicep composition) | `model1-shared.bicep` first-class alongside `model2-full.bicep`; control plane selects stack per tier | Materializes both models in IaC without a code fork |
| A2 (metering layer, ties to D19) | Per-tenant token-metering layer — APIM gateway or app-level custom metric keyed on `tenantId` | Fair allocation for Model 1 shared tier; runaway-loop guardrail for Model 2; powers usage-passthrough pricing for either |
| A3 (architectural cost controls) | Prompt caching (~50–90% off cached input), model tiering (route simple tasks to `gpt-4o-mini`), retrieval + context compression, per-tenant budgets, batch API, PAYG-first-then-PTU | Cost-efficient defaults; documented in the deployment guide (Gap 4); runtime BFF concerns |

**Reference**:
- Economic analysis: [`PROJECT-UPDATE-2026-08-12.md`](PROJECT-UPDATE-2026-08-12.md) §4–5
- Sourced pricing (Aug 2026): [`notes/pricing-research-2026-08-12.md`](notes/pricing-research-2026-08-12.md)

**Note on CLAUDE.md §6.5 protocol.** The v2→v3 evolution of D3 was originally documented as a "Path A ADR-tensions amendment" per CLAUDE.md §6.5, but v3 rewrites D3 in place to describe both tiers directly — this eliminates the "the rule says X but there's a footnote in another section that says X plus Y" reader-confusion pattern. §3A is now the *why* (economic rationale), not the *what* (the what lives in D3 itself). No CLAUDE.md §6.5 escalation required because r1 is the first project to encode D3 in code — v2 was a design draft, not a shipped constraint.

---

## 4. Three-Layer Architecture

### 4A. Tooling stack (added v3, 2026-08-12)

The provisioning pipeline is a **hybrid** stack. No single IaC/tool covers both Azure and Power Platform; the r1 design picks the right tool per layer rather than force one dialect across both. **v3.4**: under Option D (DS-1b), the *execution* vehicle for handler logic is .NET SDK/REST in-process in L2; the PowerShell scripts listed below survive as (a) parity references for the ports, (b) operator/dev tooling, and (c) the H14a sidecar payload. Only H14a executes PowerShell at provision time.

| Layer | Execution vehicle (v3.4) | Parity reference / retained script | Handlers |
|---|---|---|---|
| **Azure stamp** (per-customer RG, App Service, KV, Storage, Service Bus, OpenAI, AI Search, Doc Intelligence, App Insights, Cosmos, optional SignalR) | `Azure.ResourceManager.Resources` ARM deployment of CI-pre-compiled `customer.bicep`→ARM-JSON (+ `WhatIfAtSubscriptionScopeAsync` for structured drift detection) | `Provision-Customer.ps1` steps 1–3 (~450 effective lines; steps 4–10 duplicate other handlers' jobs) + the 25 Bicep modules (unchanged — Bicep remains the IaC authoring language) | H2a |
| **Dataverse environment lifecycle** | BAP admin REST (`api.bap.microsoft.com` … `/scopes/admin/environments`) via `HttpClient` + `DefaultAzureCredential` — the same REST sequence `Provision-Customer.ps1` STEP 5 already uses; TF Power Platform provider remains the deferred design target (M-10) | `pac admin create-environment` path retired from the runtime; H10 App User via Dataverse Web API (already in-process) | H5, H10 |
| **Managed solution import** (8 solutions, dependency-ordered) | Dataverse Web API `ImportSolution` / `StageAndUpgrade` + `ImportJob` polling; solution ZIPs are **versioned build artifacts in the publish payload** (invariant under every runtime option) | `Deploy-DataverseSolutions.ps1` (parity acceptance tests against recorded outputs — heavy port, Wave D-2) | H6 |
| **AI Search indexes** (7 canonical, 3072-dim) | `Azure.Search.Documents.Indexes.SearchIndexClient` with UAMI RBAC auth (deletes admin-key handling); index JSON schemas as content files | `scripts/ai-search/Deploy-AllIndexes.ps1` (script remains the catalog authority for the 7-index list) | H2b |
| **Config-seed layer** | YamlDotNet manifest engine + Dataverse Web API upserts in-process (the pattern H12c already uses); declarative manifest still names the authoritative source per artifact | `Invoke-SeedManifest.ps1`, per-module seeders (parity references) | H12a / H12b / H12c |
| **BFF deploy + web resources** | CI-published artifact fetch by `{buildId}` + Kudu/ARM zip-deploy + slot swap via `WebSiteSlotResource.SwapSlotAsync`; **no provision-time build** | `Deploy-Release.ps1` Phase 4 (hardened, `customerId`-driven) retained for the web-resource step | H9 |
| **Entra app registration** (~14 grants) | `Microsoft.Graph` 6.x (`Applications`, `ServicePrincipals`, `Oauth2PermissionGrants`) + `SecretClient`; app-user step via Dataverse Web API (H10 idiom) | `Register-EntraAppRegistrations.ps1` (parity acceptance tests — heavy port, Wave D-2) | H3 |
| **SPE container-type + container** | `Microsoft.Graph` `POST /storage/fileStorage/containerTypes` under `ClientCertificateCredential` (T6 cert from KV) | `Create-NewContainerType.ps1` family | H8 |
| **KV secrets / identity patch / RBAC** | `SecretClient` + `Azure.ResourceManager.AppService` (`KeyVaultReferenceIdentity` patch, both slots) + `Azure.ResourceManager.Authorization` role assignments | `AzCli*` collaborators retired | H4 |
| **Preflight quota probes** | `Azure.ResourceManager.CognitiveServices` / `.Compute` usage APIs + BAP REST + `SecretClient` | `Test-*.ps1` probe scripts | H0 |
| **E2E acceptance probes** | C# `HttpClient` probes — converges with the C3.1/C3.2 obligation to write the 11 real trap/invariant probes (same work done once); naming-conformance as pure-C# port; cost via Cost Management REST | `Validate-DeployedEnvironment.ps1`, `naming-conformance-check.ps1` | H13 |
| **Exchange ApplicationAccessPolicy (T4)** | **PowerShell — the sole residual**: `Set-ExchangeApplicationAccessPolicy.ps1` inside the EXO sidecar (§4.2a); no Graph API exists for AAP or its App-RBAC successor (verified 2026-08-18, DS-1b §0 — plan for the sidecar to live years; R22 migration is a sidecar-script change behind `IExchangePolicyApplier`) | — (the script IS the payload) | H14a |
| **Consent-capture landing** (D18) | BFF endpoint (unchanged — the one BFF touch-point) | — | H0.5 |
| **L2 orchestration** | Custom **.NET 10** control-plane service (§4.2) — REST + dispatcher + reconciler + crash recovery | — | All |
| **L3 operator UX** | `/provision-environment` Claude Code skill → L2 REST | — | — |

**Rejected alternatives**: (a) full-Terraform (no Azure module maturity match with our 26 Bicep modules; migration cost dwarfs benefit); (b) full-Bicep (no Power Platform provider); (c) Bicep + PS-only for Dataverse (v2 D14 semi-auto — inferior to TF Power Platform provider for env lifecycle); (d) fat tools container carrying pwsh+az+pac+EXO (~1.5–2 GB) — rejected as Option A per DS-1b §4/§7: az CLI's Python CVE stream, 25 stdout parsers preserving the T-trap silent-fail class, and two ambient auth sessions as permanent fleet infrastructure.

### 4.1 Layer 1 — Deterministic Handlers

Provisioning steps implemented as idempotent handlers. Each handler is a self-contained, coarse-grained operation implementing the **L2-local `IProvisioningHandler` contract** (`src/server/services/Sprk.Provisioning.ControlPlane/Handlers/IProvisioningHandler.cs`) — ADR-004-shaped (one message, one handler, one outcome) but never a compile-time reference to the BFF's `IJobHandler` (peer services). The BFF's 13 production `IJobHandler` implementations remain the *pattern exemplars* that prove the shape at scale; the L2 dispatcher mirrors `ServiceBusJobProcessor` with the §4.2b divergences.

**Handler catalog (v3, 2026-08-12)** — derived from `Provision-Customer.ps1` 13 steps + locked decisions + INVENTORY §9 config-seed layer + PROJECT-UPDATE §6 gap analysis. Splits several handlers to reflect reality (H2 → H2a/b/c; H12 → H12a/b/c) and adds H0.5 for Model 2 consent-capture (D18).

**Idempotency key `{schemaVer}` semantics (I3 resolved v3)**: version tokens are **deterministic content hashes / semantic versions of the artifact being deployed**, not run-attempt counters. `{bicepVer}` = git SHA of `infrastructure/bicep/`, `{solutionVer}` = solution version manifest hash, `{configVer}` = seed manifest hash, `{buildId}` = BFF CI build number. This makes re-running the same handler with unchanged inputs a no-op. Three-level idempotency (v3.4 precise form): **L1** — Service Bus duplicate detection on `MessageId = SHA256(HandlerId|RunId|CustomerId|paramHash|attempt)` (queue property `requiresDuplicateDetection: true`; the `attempt` term keeps §4C retries deliverable, §4C); **L2** — Redis dispatch lock at the dispatcher dequeue path; **L3** — durable dedup via the Cosmos `completedPhases` scan in each handler body (+ Dataverse alternate-key upserts where applicable). Runtime classification per handler: **§4.1b**.

| # | Handler | Source logic | Gate | Idempotency key |
|---|---------|-------------|------|-----------------|
| H0 | Preflight / validate inputs **+ lead-time quota checks (v3.2)** — Azure OpenAI regional TPM (150+200+30+350 per model deployment ≤ subscription quota in target region); Dataverse env-creation rate (per-tenant ≤4/hour typical); Subscription vCPU + storage quota for the target region; SPE container-type replication lead-time (fail fast if new tenant with no cert-bootstrap done) | Step 1 + runbook checklist + new `az cognitiveservices ...`, `pac admin quota`, `az vm list-usage` calls | **All quota checks pass** (blocks the run before H1; §9 north-star lead-time items surfaced BEFORE pipeline starts, not after 30-min Bicep) | `preflight-{customerId}-{paramHash}` |
| **H0.5 (v3)** | **Consent-capture callback** (Model 2 self-service only) — **v3.2 re-consent semantics**: if `sprk_dataverseenvironment` row exists with same `tid` + status ∈ {Ready, Running, WaitingOnGate}, no-op with 200 + link to existing run; if Failed/Cancelled, restart from H0 | BFF `/api/onboarding/consent-callback` endpoint (D18) captures `tid` on admin consent, seeds run parameters, checks re-consent | — | `consent-{customerId}-{tid}` |
| H1 | Subscription readiness | NEW (D4) — ARM verification | **Lighthouse delegation** (CustomerOwned) | `subready-{customerId}` |
| **H2a (v3, was H2 · v3.2 corrected scope)** | Resource group + Azure infra (per-customer Bicep). **6 additions vs current `Provision-Customer.ps1` step 3** (which only does Storage+KV+ServiceBus): **Cosmos DB** (BFF prereq, R11), **OpenAI**, **AI Search**, **Document Intelligence**, **App Insights + Log Analytics**, **optional SignalR**. **Redis REMOVED per Q-E FR-12** (v3.2 M-1 reconciliation — Redis is per-environment, deployed via `scripts/Deploy-RedisCache.ps1`, NOT per-customer). **UAMI first-class** via new `uami.bicep` module + `app-service.bicep` refactor (Phase C — v3.2 A3 correction of v3.1's "already done" claim). | Steps 2–3, `customer.bicep` + modules (or `model1-shared.bicep` per §3A A1) — 6 new module invocations vs today | — | `infra-{customerId}-{bicepVer}` |
| **H2b (v3, new · v3.2 path corrected)** | **AI Search index provisioning** (7 canonical indexes per FR-07 catalog — `spaarke-files-index`, `spaarke-discovery-index`, `spaarke-records-index`, `spaarke-rag-references`, `spaarke-insights-index`, `spaarke-session-files`, `spaarke-invoices-index`; `spaarke-playbook-embeddings` RETIRED per spaarke-ai-architecture-redesign-r1 task 035 / FR-P2-06; `spaarke-knowledge-index` archived under `_archive/`; 3072-dim vectors) | **`scripts/ai-search/Deploy-AllIndexes.ps1`** (v3.2 path fix — v3.1 said `infrastructure/ai-search/`; that dir has only the schema JSONs). Runs post-H2a; script is catalog-driven + PUT-idempotent. | — | `aisearch-{customerId}-{indexVer}` |
| H3 | Entra app registration + OBO credential. **v3.5 split (2026-08-19, per auth-v4 MI-FIC coordination — FR-39)**: **Model 1** branch is a **no-op for the BFF app-reg itself** — the single shared multitenant app-reg (`AzureADMultipleOrgs`) already exists, created once, not per customer; H3 instead (a) verifies the shared app-reg's **~14 permission grants per `GraphAppRoles.cs`** are current and (b) registers the customer tenant's service principal via the H0.5/D18 consent-callback (I6-enforced: app-reg selection is per-tenant-request-context-derived, no default/fallback). **Model 2** branch provisions a **per-customer** BFF app-reg (**~14 permission grants per `GraphAppRoles.cs`** — v3.2 corrected from v3.1's "~11") **+ a federated identity credential (FIC)** trusting the shared BFF UAMI (issuer = customer tenant OR Spaarke tenant depending on Spaarke-hosted vs customer-owned; subject = UAMI `principalId` NOT `clientId`; audience `api://AzureADTokenExchange`; `AADSTS70021` retry logic) per auth-v4 §3.1 recipe — the client-secret path is retained as an ordered fallback until auth-v4's Phase 5 (FR-39 pluggability contract). **v3.2 (r3 task 060, unchanged by v3.5)**: vestigial Dataverse S2S app-reg DROPPED — zero code consumers; do not provision, either model. | `Register-EntraAppRegistrations.ps1` (D2) — hardened to idempotent + tenant-aware (Model 1 vs Model 2); FIC-creation extension per auth-v4 §3.2 (r1 invokes the extended script rather than duplicating logic, if it lands before Wave G-3 dispatches) | **Model 2**: admin consent granted (Graph query) + FIC exchange verified (not creation-200 alone). **Model 1**: shared app-reg consent already verified; consent-callback records the new customer tenant's trust. | `appreg-{customerId}-{tenantId}` (Model 2) / `appreg-shared-consent-{tenantId}` (Model 1) |
| H4 | Key Vault secrets population + **`keyVaultReferenceIdentity` PATCH to UAMI** (T1) + **canonical naming applied at seed time** (v3.2 Phase G — per r3 task 063 standard: `sprk-{env}-kv` vault + env-agnostic secret names + `spaarke-spekvcert` DO-NOT-RENAME dev exception) | Step 4 + PATCH; secrets stored as KV URI refs (B3); seeder driven by **canonical secret-catalog manifest** (Phase H per r3 KV federation design Phase 3b) | — | `kv-{customerId}-{secretsVer}` |
| **H5 (v3 design intent, v3.2 deferred exec)** | Dataverse environment creation — **design target = TF Power Platform provider (D14)**, but implementation deferred to first-customer engagement per v3.2 M-10 (dev-only reality, no customer volume). **Interim (Phase A/B)**: continue with `pac admin create-environment` PS invocation + gate-verified. When first customer signs, TF migration lands as its own task chain. | Interim: `pac admin`; target: TF `powerplatform_environment` resource | — | `dvenv-{customerId}` |
| H6 | Solution export/fix (managed) + Package Deployer import (~10 solutions, dependency-ordered) | Export (D1) + `Deploy-DataverseSolutions.ps1` + Package Deployer | — | `solimport-{customerId}-{solutionVer}` |
| H7 | 7 Dataverse env-var values + BFF app-settings — **v3.2 (Phase H)**: token substitution pattern superseded by canonical secret-catalog manifest + KV federation reader (BFF startup reads from KV directly with SDK caching); `#{TOKEN}#` substitution retained during transition | Step 8 + template evolution per Phase H | — | `envvars-{customerId}-{configVer}` |
| **H8 (v3, confidential-client · v3.3 SPE privilege footnote)** | SPE container type + root container | Existing scripts + **switch to confidential-client (app-only) token** — delegated token now 403s (`public client not allowed`). Cert bootstrapped from KV. **v3.3 SPE privilege footnote**: `FileStorageContainerType.Manage.All` no longer requires SPE-Admin / Global-Admin as of June 2026 per Q5 research spike ([notes/graph-spe-2026-08-standards-spike.md](notes/graph-spe-2026-08-standards-spike.md)) — owning-tenant bootstrap simpler; runbook detail, not a code change. | Container-type replication (up to 24h — **lead-time item, not in-pipeline wait**; §9 north star; H0 preflight checks cert-bootstrap done) | `spe-{customerId}` |
| H9 | BFF deploy + app settings + **`Deploy-Release.ps1` Phase 4 hardened** (Gap 2 — `customerId`-driven, no `spaarkedev1` hardcode) | **v3.4 artifact-based**: fetch CI-published artifact by `{buildId}` + zip-deploy + slot-swap (no provision-time `dotnet publish` — forbidden per FR-12) + hardened `Deploy-Release.ps1` Phase 4 for web resources (Gap 2 — `customerId`-driven, no `spaarkedev1` hardcode). r3 gates run in CI against the artifact; H9 verifies artifact metadata. | — | `bff-{customerId}-{buildId}` |
| **H10 (v3 design intent, v3.2 deferred exec · Graph-parity from code constant; figure + dual-row contract corrected 2026-08-26 per task 205d A41)** | Dataverse Application User (BFF app-reg + UAMI, **TWO systemuser rows per environment** — auth-v4 §10.4) + **Graph app-role parity from `GraphAppRoles.cs` constant** (T3 v3.2). Design target = TF `powerplatform_user`; implementation deferred with H5 to first-customer engagement per M-10. Interim: PPAC UI fallback + verification query. **`GraphAppRoles.cs` is the source of truth for the GUID-completion figure — do NOT hardcode a count here.** As of task 144 (2026-08-20) the catalog holds 15 role entries; per the class's own remarks, all 15 `AppRoleId` GUIDs are populated (14 by r1 task 005 on 2026-08-17 + the 15th, `UserInviteAll`, by task 144). H10's §8 escalation gate re-checks this live on every run and refuses to proceed (before any Graph/Dataverse write) if the catalog ever regresses to a null `AppRoleId`. **Per-model identity source for the UAMI row's `azureactivedirectoryobjectid`** (auth-v4 §10.4 BINDING — MUST be the principalId, NEVER the clientId): **Model 1** = shared app-reg appId + shared UAMI (`sprk-{env}-shared-bff-uami`), `azureactivedirectoryobjectid = shared UAMI principalId`; **Model 2** = per-customer app-reg + per-stamp UAMI (`mi-spaarke-{customerId}-{env}`), `azureactivedirectoryobjectid = that stamp's UAMI principalId`. | Interim: PPAC UI + Graph SDK for role sync; target: TF `powerplatform_user` | — | `appuser-{customerId}` |
| H11 | User provisioning (identity preset) | r1 registration flow (D6) | **B2B consent** (B2BGuest only) | `users-{customerId}` |
| **H12a (v3, PROMOTED from thin)** | **AI seed chain**: type-lookups → actions → tools → knowledge → skills → playbooks → output-types → **playbook consumers** (single AI routing surface, ADR-039) | Existing `scripts/seed-data/Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1`; **authoritative source per artifact declared in seed manifest** (resolves the `scripts/seed-data` MVP vs `infra/dataverse` R7 drift per INVENTORY §9) | — | `aiseed-{customerId}-{seedVer}` |
| **H12b (v3, PROMOTED · v3.2 DAG-parallel with H12a)** | **App-config seed**: DataGrid configs, field-mapping profiles + rules, system workspace layouts, chart definitions | Existing per-module PS seeders + Web-API seeding recipes (per `FIELD-MAPPING-ADMIN-GUIDE.md`); declarative manifest. **No dependency on H12a** — grid/field-mapping/workspace-layout seeds don't need AI seed to be complete first. | — | `configseed-{customerId}-{configSeedVer}` |
| **H12c (v3, PROMOTED)** | **Runtime references**: AI model deployment records (`sprk_aimodeldeployment`) point to this customer's Azure OpenAI deployment | Env-specific; runs after H2a (OpenAI deployed) + H12a (aitype lookups seeded) | — | `runtimerefs-{customerId}-{modelVer}` |
| H13 | End-to-end acceptance gate (Gap 4) | Extended `Validate-DeployedEnvironment.ps1` — asserts effects, not intentions (R7); checks BFF `/health`, sample analysis, sample document upload+index, workspace-layout render, wizard field-map, **all 6 §4B silent-fail traps cleared** | **Validation passed → registry `Ready`** | `validate-{customerId}-{buildId}` |
| **H14 (v3.2, S2S sub-step removed per r3 task 060)** | **Post-deploy integration wiring** (I1 resolved) — enumerated: (a) **two Exchange ApplicationAccessPolicies** (BFF app-reg + UAMI — T4 with action semantics: create-if-missing then verify), (b) Graph webhook subscriptions per Communication/Email module (with HMAC signing keys from H4), (c) service endpoint webhooks (Dataverse → BFF). **v3.2**: sub-step (d) S2S consent flows REMOVED (r3 task 060 dropped the S2S app-reg). Sub-steps (a)/(b)/(c) are **DAG-parallel** — no cross-dependencies given H4/H10/H12 outputs available. | New scripting; each sub-step idempotent | — | `integrations-{customerId}-{integrationVer}` |

**Handler dependencies** (DAG, v3.2 parallelism corrected):
```
H0 → H1 → H2a → { H2b (indexes), H4 (KV), H5 (dv-env) }  # 3-way parallel post-Bicep
              ↓
              H4 → H3 (needs KV for secrets storage) → { H8 (SPE), H9 (BFF deploy) }
                                                       ↓
H5 → H6 (solutions) → H7 → H10 (app-user, needs H6 solutions) → H11
                                    ↓
                            { H12a (AI seed), H12b (config seed) }  # v3.2: parallel — H12b doesn't need H12a
                                    ↓
                            H12c (runtime refs — needs both H12a + H12b + H2a OpenAI)
                                    ↓
                            H14 { (a) Exchange×2, (b) Graph webhooks, (c) service-endpoint webhooks }  # sub-steps parallel
                                    ↓
                            H13 (final gate)
```

**Model 2 self-service branch**: `H0.5 (consent-capture) → H0 → …` — the pipeline starts on consent-callback rather than operator-initiated. **Re-consent (v3.2)**: H0.5 no-ops on active/completed runs; only restarts from H0 on failed/cancelled state.

### 4.1b Handler runtime classification — Option D (added v3.4 per DS-1b)

Locked 2026-08-18: the runtime is **Option D hybrid** — every collaborator with an SDK/REST equivalent executes as pure .NET in-process in L2; the single platform-forced residual executes in the EXO sidecar (§4.2a). Of ~29 shell-out collaborators audited across 13 handlers (DS-1b §1, per-collaborator file:line evidence there), **exactly one** has no .NET equivalent.

| Class | Definition | Handlers | Count |
|---|---|---|---|
| **A — pure .NET** | Every collaborator has an SDK/REST equivalent | H0, H2a, H2b, H3, H4, H5, H6, H8, H9 (post-artifact-re-scope), H12a, H12b, H13 | 12 |
| **C — mixed** | One residual PS collaborator among SDK-capable ones | H14 (H14a only; H14b/c already in-process REST) | 1 |
| **in-process already** | Never shelled out | H0.5, H1, H7, H10, H11, H12c | 6 |

Per-handler SDK surface (packages already largely in the BFF/L2 dependency set):

| Handler | Primary .NET surface |
|---|---|
| H0 | `Azure.ResourceManager.CognitiveServices` + `.Compute` usage APIs; BAP admin REST; `Azure.Security.KeyVault.Secrets.SecretClient` |
| H2a | `Azure.ResourceManager.Resources` (ARM deploy of CI-pre-compiled Bicep→JSON + `WhatIf` structured drift); `.AppService` (T1 identity read); `SecretClient` |
| H2b | `Azure.Search.Documents.Indexes.SearchIndexClient` (UAMI RBAC — admin-key handling deleted) |
| H3 | `Microsoft.Graph` 6.x (`Applications`/`ServicePrincipals`/`Oauth2PermissionGrants`); `SecretClient`; Dataverse Web API (`HttpClient`) |
| H4 | `SecretClient`; `Azure.ResourceManager.AppService` (`KeyVaultReferenceIdentity` PATCH both slots); `.Authorization` (role assignments) |
| H5 | BAP admin REST via `HttpClient` + `DefaultAzureCredential` (the `Provision-Customer.ps1` STEP 5 sequence ported) |
| H6 | Dataverse Web API `ImportSolution`/`StageAndUpgrade` + `ImportJob` polling; solution ZIPs as versioned publish-payload artifacts |
| H8 | `Microsoft.Graph` `fileStorageContainerTypes` under `ClientCertificateCredential` (T6); `SecretClient` |
| H9 | Artifact fetch by `{buildId}` + Kudu zip-deploy / `Azure.ResourceManager.AppService`; `WebSiteSlotResource.SwapSlotAsync` |
| H12a | YamlDotNet + Dataverse Web API (H12c's existing in-process pattern) |
| H12b | Dataverse Web API upserts (~40-line mechanical ports); the two deferred seeders (field-mapping, chart-def) authored directly in C# |
| H13 | `HttpClient` probe suite (converges with the 11 real T/I probes owed under C3.1/C3.2); pure-C# naming-conformance port; Cost Management REST |
| H14 | H14b/c in-process REST (unchanged); **H14a → `ExchangePolicySidecarClient : IExchangePolicyApplier` → sidecar HTTP** (§4.2a) |

**Wave sequencing** (DS-1b §7): **Wave D-1** — dispatcher (§4.2b) + sidecar + the 9 thin az-one-liner SDK swaps + H0/H2b/H5/H12a/H12b/H13 ports + H9 artifact re-scope (~10 of 13 shell-out handlers executable). **Wave D-2** — H3, H6, H2a heavy ports with parity acceptance tests against recorded script outputs. Bounded fallback if a hard commercial date lands mid-wave: run those scripts temporarily in the sidecar (it has pwsh; add nothing but the scripts) — a contained concession, never a main-site shell-out.

### 4.1a Model 1 vs Model 2 handler behavior differences (added v3.2)

Handlers execute the same code but with different inputs and post-conditions depending on `tenancyModel`. This table enumerates what changes; handlers not listed behave identically across tiers.

| Handler | Model 2 (dedicated) | Model 1 (shared trial/SMB) |
|---|---|---|
| **H0 preflight** | Full per-customer quota check (subscription vCPU, OpenAI regional TPM headroom for dedicated deployment) | Verify per-tenant token budget (`tokenBudgetMonthlyUSD` per D19); confirm shared-platform OpenAI quota has capacity for +1 tenant |
| **H2a Bicep composition** | `customer.bicep` (or `model2-full.bicep` stack) — full dedicated stamp; new UAMI, KV, Cosmos, Storage; **dedicated App Service Plan + AI Search + OpenAI** | `model1-shared.bicep` stack — dedicated KV/Cosmos/Storage/UAMI **only**; **shares platform App Service Plan + AI Search + OpenAI** (per §3A A1) |
| **H2b AI Search indexes** | 7 canonical indexes on customer's dedicated AI Search service; per-customer index storage | 7 canonical indexes ALREADY exist on shared platform AI Search service — H2b **verifies** presence and provisions per-tenant `tenantId`-filter query template (does NOT re-create indexes) |
| **H4 KV secrets** | Customer's dedicated KV (`sprk-{customerId}-{env}-kv` per Phase G naming standard) | Customer's dedicated KV (naming: still per-customer since KV is 🔴 dedicated per §3A A1) |
| **H7 env-var values + BFF app-settings** | Points at customer's dedicated OpenAI/AI Search/App Insights endpoints | Points at shared platform OpenAI/AI Search endpoints; per-tenant metering headers set via D19 token-metering layer |
| **H10 Dataverse App User** | UAMI registered as System Administrator on customer's dedicated Dataverse env | UAMI registered on customer's dedicated Dataverse env (Dataverse remains 🔴 dedicated per §3A A1) |
| **H12c runtime refs** | `sprk_aimodeldeployment` rows point at customer's dedicated OpenAI deployment | `sprk_aimodeldeployment` rows point at shared platform OpenAI deployment with per-tenant metering attribution |
| **H13 acceptance** | Full E2E + verifies dedicated resource isolation (no cross-customer data visible in sample queries) | Full E2E + verifies **`tenantId`-filter enforcement** on every AI Search query + verifies token metering attribution works |

**Trial-environment provisioning (v3.2 per H-6 decision)**: Phase F Acceptance stands up a fresh **`trial-{yyyymmdd}` customer stamp** using Model 1 profile (`spaarke-hosted-model1-trial`) for E2E validation. Independent of demo/prod (decommissioned for budget per r3 handoff). Cleanup after acceptance is discretionary.

### 4C. Rollback semantics on partial failure (added v3.2)

Idempotency + resumability (D11) covers the happy path where a failed handler can be re-run to completion. This section covers the different case: a handler completes but its output leaves the environment in a state that a downstream handler cannot proceed from, OR the operator abandons the run.

**Handler failure classification**:

| Class | Definition | Example | Recovery |
|---|---|---|---|
| **Resumable** | Handler failed before writing any external side effect (or wrote to Cosmos only) | H0 preflight fails on missing quota; H3 fails on 429 from Graph | Cosmos run marked `Failed`; operator resolves external precondition; `POST /api/runs/{id}/resume` restarts the failed handler |
| **Retryable-with-cleanup** | Handler wrote a partial external side effect that its own idempotency key handles (upsert or check-first) | H6 imports 3 of 10 solutions then Package Deployer throws on solution 4 | Re-run H6 — Package Deployer skips already-imported solutions by version; resumes at solution 4 |
| **Quarantine-required** | Handler wrote a partial external side effect that is NOT self-healing on re-run and cannot proceed to next handler | H2a Bicep deploys 12 of 16 resources then fails on OpenAI quota; a corrupt Dataverse env state that TF cannot import back | Cosmos run marked `Quarantined`; environment is NOT torn down (decommission out of scope per D17) but NOT usable; operator must manually resolve OR mark for `Decommission-Customer.ps1` teardown; new run against same `customerId` blocked until quarantine cleared |
| **Successful-but-drifted** | Handler completed successfully but downstream config drift (e.g., human edited KV/App Service between runs) invalidates the state | Rare; H7 wrote env vars, human edited App Service settings, H9 verify fails | H13 acceptance detects; operator re-runs affected phases with `resumeFromPhase` param |

**Cosmos state transitions on failure**:
- `Running` → `Failed` (handler threw, retryable)
- `Running` → `Quarantined` (handler wrote un-recoverable partial state)
- `Failed` → `Running` (operator called `resume_run`)
- `Quarantined` → `Cancelled` (operator explicitly abandons, may follow with decommission)
- `Cancelled` → clears `sprk_currentrunid` on registry row; environment record marked `SetupStatus=Failed` (Dataverse choice)

**Retry envelope (v3.4)**: re-dispatch after `Failed → Running` (resume) or reconciler-driven retry is a **fresh enqueue with `attempt` incremented**. `attempt` participates in the deterministic MessageId hash, so L1 duplicate detection (ON as of v3.4) never swallows a legitimate §4C retry issued inside the PT1H dedup window, while true duplicates (same attempt — racing reconciler instances) still collapse to one delivery. The dispatcher never uses SB Abandon as a retry mechanism (§4.2b) — §4C is the sole retry authority.

**Cross-customer serialization on quarantine**: `sprk_currentrunid` stays set on the `sprk_dataverseenvironment` row while status is `Quarantined` — blocks new runs against the same customer until the operator explicitly clears (via new `POST /api/runs/{id}/clear-quarantine` endpoint). Cross-customer runs unaffected.

**What's explicitly NOT in scope (per D17)**: automated rollback that re-creates a pristine state. Rollback = quarantine + operator decision (repair or teardown). This matches Terraform semantics (`terraform destroy` is a separate operator action, not automatic on `apply` failure).

### 4D. Tenant Isolation Invariants (added v3.3 per Q6 concern)

Customer-provisioning risk analysis surfaces one class of catastrophe that must be structurally impossible: **cross-tenant data bleed** — one customer's SPE container ID / KV secret / query filter accidentally scoped to a different tenant leads to unauthorized file access, PII disclosure, or (in the legal domain we serve) privileged-communication leak. This section states the invariants r1 must uphold + how each is enforced at code level, not just docs.

**Five binding invariants**:

| # | Invariant | Enforcement mechanism | Verification |
|---|---|---|---|
| **I1** | **No hardcoded default tenant in provisioning scripts** — every script that provisions per-customer must require `-TenantId` explicitly; no fallback default | Script parameter definitions carry `[Parameter(Mandatory=$true)]`; **v3.3 code fix**: `Register-EntraAppRegistrations.ps1:63` currently has `[string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"` (Spaarke tenant hardcoded) — remove default; make mandatory | Pre-commit ArchTest: grep-scan provisioning scripts for tenant-shaped GUID defaults; fail on hit |
| **I2** | **All AI Search queries include unconditional `tenantId` filter** — regardless of index, regardless of query shape | BFF services build OData filter with `tenantId eq '{ctx.TenantId}'` predicate ALWAYS present; Fable H-2 confirmed for `ReferenceRetrievalService` (line 316) + `RecordSearchService` FR-12 (line 257) | **New ArchTest** (per r3 task 040 pattern) — scan BFF for any AI Search client `.Search(...)` call whose filter doesn't include `tenantId eq`; fail on hit. Phase A audit: full BFF audit beyond spot-checked services |
| **I3** | **All Cosmos reads/writes include `/tenantId` partition key predicate** — no cross-partition queries against tenant-scoped containers (AI sessions, prompts, audit) | Cosmos SDK usage MUST specify `PartitionKey` on every operation; cross-partition queries flagged in code review | **New ArchTest** — scan for Cosmos SDK `.ReadItemAsync(...)` / `.CreateItemAsync(...)` calls without explicit `PartitionKey`; fail on hit. Existing ProvisioningRun container (`/customerId`) already conforms per §6.2 |
| **I4** | **SPE container IDs are always tenant-scoped-derived, never fallback default** — the BFF's SPE-related code paths MUST read the container ID from the current tenant's env-var (`sprk_SharePointEmbeddedContainerId`) or KV secret (`customer-{customerId}-spe-container-id`); no fallback default | BFF code review + ArchTest: fail on any string literal matching SPE container ID pattern (`b!...`) in BFF services; container-ID resolution goes through a single `ITenantContainerResolver` service with per-request tenant context | Phase A audit: enumerate every SPE call site (`FileStorageContainer.Selected`, `.Files.ReadWrite.All`) + verify all derive container from tenant context |
| **I5** | **Graph token acquisition is per-tenant scoped** — delegated calls use OBO with the caller's `tid` claim; app-only calls use `.default` scope with the target tenant's `tid` explicitly named (NOT the default tenant of the MI credential) | `GraphClientFactory` must accept a `tenantId` parameter on every token acquisition; no code path uses `DefaultAzureCredential()` without explicit tenant scoping | New ArchTest — scan `GraphClientFactory` for token acquisition without explicit `tenantId`; fail on hit |
| **I6 (Model 1 only — added v3.5, 2026-08-19, per auth-v4 §5.4 proposal, adopted)** | **The app registration used for an OBO exchange MUST be derived from per-tenant request context; no default or fallback app registration.** Under MI-as-FIC (FR-39), Model 1's shared BFF UAMI can mint an assertion for ANY app registration that trusts it — Model 1's isolation boundary, previously resource-level (BFF reads customer X's secret from customer X's Key Vault), becomes **code-level**: nothing but correct tenant routing stops the process authenticating as the wrong customer's app-reg. Scoped to Model 1 only — Model 2's per-customer app-reg makes this structurally true by construction, so the invariant is a no-op-but-still-verified there. | The code path resolving which app-reg to use for an OBO token exchange takes tenant context as an explicit, non-defaultable parameter | **New ArchTest** `Spaarke.ArchTests.TenantIsolation.I6_ObApp*` (same pattern as I1–I5) — scans OBO-exchange call sites for app-reg resolution without explicit per-tenant-context derivation; fail on hit |

**Why each invariant matters** (the "cost of doing nothing" per CLAUDE.md §11):

- **I1**: an operator running the provisioning script for a new customer without passing `-TenantId` would provision the customer's Entra app-reg in the **Spaarke tenant**, granting Spaarke's users access to the customer's Dataverse env. Data-bleed severity: HIGH.
- **I2**: a missing `tenantId` filter on an AI Search query returns documents from ALL customers' indexed content — a legal firm's motion drafts returned to a different firm. Severity: CATASTROPHIC (legal privilege leak).
- **I3**: a cross-partition Cosmos query returns AI conversation history from other customers. Severity: HIGH (conversational PII).
- **I4**: a fallback default SPE container ID would put a customer's file uploads into another customer's SPE container. Severity: CATASTROPHIC (privileged docs in wrong hands).
- **I5**: token acquired against wrong tenant returns Graph resources (SPE files, mail, group membership) from wrong tenant. Severity: CATASTROPHIC.
- **I6** (Model 1 only): a hardcoded or fallback app-reg selection in the OBO exchange path would let the shared BFF UAMI mint an assertion for the wrong customer's trust relationship — the process authenticates AS the wrong customer. Severity: HIGH (misrouted OBO exchange; not CATASTROPHIC only because Model 1's app-reg is a single shared object today, limiting blast radius relative to I2/I4/I5's per-customer-container-or-index scenarios — still load-bearing and worth the same enforcement discipline).

**Verification lifecycle**:

- **At code time**: 6 ArchTests (I1–I5 v3.3 + I6 v3.5; new; sequence into r3 forcing-functions ecosystem via CI-wiring coordinated PR with `ci-cd-unit-test-remediation-r1`)
- **At provisioning time**: H13 acceptance samples a query in each of the 6 invariant classes to prove filter enforcement
- **At runtime**: OpenTelemetry span attributes include `tenantId`; log samples cross-referenced against expected tenant for anomaly detection
- **Ongoing**: Phase A audit sweep of every BFF service touching AI Search / Cosmos / Graph / SPE — beyond the 2 spot-checked services in the Fable pass

**What this section is NOT**: it does not defend against MALICIOUS insider abuse (an authenticated Spaarke operator with cloud-admin privileges can bypass any structural control). r1's threat model is honest-but-buggy code + operator error — not adversarial internal access. External-actor threat model (unauthenticated cross-tenant abuse from the internet) is covered by CORS + AAD auth + per-request `tid` validation — separate concerns handled by ADR-028 auth architecture.

### 4B. Silent-failure trap catalog (added v3, 2026-08-12)

Six known-issue guardrails baked into handlers as **verified post-conditions**, not runbook footnotes. Each trap has been diagnosed in production; ignoring any of them results in a BFF that boots but fails silently in a specific code path. Handlers assert the trap is cleared before reporting success.

| # | Trap | Where it bites | Handler that owns the fix | Verification |
|---|---|---|---|---|
| **T1** | **`keyVaultReferenceIdentity` not PATCHed to UAMI** — App Service resolves `@Microsoft.KeyVault(...)` refs with the wrong identity → all KV-ref settings become `null` at runtime | H4 completes but BFF startup fails resolving `Dataverse:ClientSecret` etc. | H4 | ARM read: App Service `keyVaultReferenceIdentity` == UAMI resource ID. **Post-D20 (r3 task 061)**: fail-fast config validation (`ValidateDataAnnotations().ValidateOnStart()` on 24 Tier-1 IOptions classes) catches missing KV-resolved settings at BFF startup — `/health` probe fails → deploy fails visibly instead of first Dataverse call. **Requires Phase C UAMI migration** — see T5. |
| **T2** | **MI not registered as Dataverse Application User** in the target env | Every BFF → Dataverse call 403s → surfaces as 500 to callers; Communication/Email module fails silently on subscription setup | H10 (Phase C TF-driven per D14) | Dataverse query: `systemusers?$filter=applicationid eq {mi-app-id}` returns 1. **Post-D20 (r3 task 061)**: DataverseOptions `[Required]` on ClientSecret + ValidateOnStart surfaces missing config at boot; MI-App-User missing is a runtime 403 that r3's fail-fast doesn't cover directly, so **H10 verification query stays as primary defense**; r3 lighter safety net. |
| **T3** | **UAMI Graph app-role parity broken** — the **14** Graph app-roles required per `Infrastructure/Auth/GraphAppRoles.cs` (r3 task 062 constant) are NOT all replicated onto the UAMI service principal | App-only Graph calls from BFF (SPE, mail, groups, Teams) 403 despite delegated flow working | H10 (post-step, code-constant driven) | Graph query: UAMI SP `appRoleAssignments` includes all 14 role IDs from `GraphAppRoles.cs`. **Post-D20 (r3 task 062)**: expected role list IS the compile-time constant `GraphAppRoles.cs`; H10 reads the constant + syncs UAMI SP + fails deploy on parity mismatch. **⚠️ Class currently has 11 of 14 `AppRoleId` GUIDs = null pending live enumeration of Graph resource SP** — r1 H10 escalation obligation: complete the constant before first production customer provisioning. Nightly parity check queued behind CI-workflow wiring per r3 `task-042-063-ci-gate-wiring-deferral.md` (coordinated PR with `ci-cd-unit-test-remediation-r1`). |
| **T4** | **Only one Exchange ApplicationAccessPolicy** created (BFF app-reg, missing UAMI) — app-only mail calls scope-fail | Email/Communication module ingestion 403s despite delegated Mail.Send working | H14 (enumerated, action-and-verify) | Exchange `Get-ApplicationAccessPolicy` returns 2 entries (both principals). **H14 action semantics (v3.2)**: on 0 or 1 policies present, H14 CREATES the missing policy (not verify-only); on 2+ present, H14 verifies AppIds match expected principals or fails with drift diagnostic. **v3.4**: H14a executes via the EXO sidecar (§4.2a); T4's action-and-verify semantics are byte-identical — the same get-before-set script runs unchanged inside the sidecar, and `ExchangePolicySidecarClient` maps its JSON result envelope onto the same create-if-missing / verify-drift-diagnostic branches. |
| **T5** | **Slot-per-slot System-Assigned MI parity** — System-Assigned MI is intrinsically per-slot, so KV RBAC granted only to prod slot → staging deploys can't resolve KV refs → cold-start failures on slot swap. **Design v3.1 wrongly asserted UAMI adoption was already done — v3.2 correction**: current pattern IS System-Assigned per `infrastructure/bicep/modules/app-service.bicep` (`identity: enableManagedIdentity`), no `uami.bicep` module exists yet | Production deploy triggers a 503 window post-swap | H4 (interim: RBAC-both-slots) + **Phase C UAMI migration (structural fix)** | **Interim**: ARM read of BOTH slots' MI object IDs; KV RBAC check for both — same fix works because both slots' System-Assigned MIs are distinct principals. **Structural fix (Phase C)**: new `uami.bicep` module + `app-service.bicep` refactor to consume UAMI + bind to both slots → T5 becomes intrinsically impossible (single UAMI spans both slots). |
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

**Concurrency (I5 resolved v3 · v3.4 transport half added)**: same-customer serialization has two cooperating halves. **Admission**: optimistic concurrency on Dataverse `sprk_dataverseenvironment.sprk_currentrunid` (`null → newRunId` conditionally; conflict → 409 with the winning run ID) — authenticated via Path X (§9.6). **Transport**: the §4.2b session dispatcher guarantees at most one handler executing (and therefore at most one handler-writer on the ProvisioningRun document) per customer at any instant. Cross-customer runs execute in parallel (own Cosmos partition + Dataverse row + SB session). Handlers KEEP their `ReplaceRunAsync` Conflict arms: sessions eliminate handler∥handler races, not handler∥operator races (cancel / gate-advance / clear-quarantine endpoints and the reconciler outcome-applier remain concurrent writers; the log-or-`Resumable` posture is the now-rare backstop). Documented flip path if per-customer concurrent dispatch is ever required (SLA < ~3 h — arithmetically impossible while the 24 h SPE gate exists): **Cosmos conditional-patch append** (server-side atomic check-and-append), NOT ETag-retry loops; do not pre-build (DS-2b §2b/§9).

**Crash recovery (I6 resolved v3)**: On startup, L2 scans Cosmos for `status ∈ {Running, WaitingOnGate}` runs older than 2× median-handler-duration. For each orphaned run, L2 enqueues a `HandlerEnvelope` job to resume from `currentPhase`. Handlers are idempotent (three-level: MessageId dedup + Redis idempotency lock + deterministic idempotency key per §4.1), so a duplicate-resume post-crash is safe.

**Handler execution model (v3.2 added · v3.4 CORRECTED + restructured)**: App Service's 230 s HTTP timeout means L2 REST endpoints cannot synchronously invoke long handlers. The execution model is **fire-and-forget via Service Bus + L2-owned session dispatcher + state-reconciler**, all hosted in the L2 control plane:

1. **HTTP endpoint** (e.g., `POST /api/runs/{id}/resume`) validates, writes intent to Cosmos, enqueues a `HandlerEnvelope` (carrying `HandlerId`, `RunId`, `CustomerId`, `paramHash`, **`attempt`** — §4C) to `sprk-provisioning-jobs`, returns 202 Accepted. Roundtrip <100 ms.
2. **Handler execution happens in L2's own dispatcher** (§4.2b) — `ProvisioningHandlerDispatcher` consumes the queue session-serialized per customer, resolves the handler by `HandlerId` via keyed DI, invokes it in-process (pure .NET per §4.1b; H14a via the §4.2a sidecar), and applies the outcome to Cosmos via the §4C taxonomy. *(v3.4 correction: v3.2–v3.3 said "the BFF's existing `IJobHandler` infrastructure" — that contradicted D8/D12, the spec MUST rules, and the implementation, and left the consumer unowned; the BFF has zero role in provisioning execution — its ServiceBusJobProcessor drains a different queue and never registers provisioning handlers.)*
3. **State-reconciler `BackgroundService`** polls Cosmos every 5 s, computes the DAG ready-set from `completedPhases`, and enqueues ready handlers with the appropriate `attempt`. This advances the pipeline without blocking any HTTP request.
4. **Client polling** unchanged (`GET /api/runs/{id}`, 15–30 s interactive cadence).

**Why not Durable Functions**: ADR-004 rejects them; the state-machine-in-Cosmos + handler-worker pattern (L2's `IProvisioningHandler` contract) is proven at scale by the BFF's 13 production `IJobHandler` handlers. Trade-off: L2 must actively poll Cosmos for DAG advancement rather than a workflow runtime doing it; acceptable at provisioning cadence (single-digit runs/day).

**Concurrency safety in the reconciler**: multiple L2 instances each run the reconciler; duplicate enqueues collapse at L1 (duplicate detection on the deterministic MessageId — same `attempt` ⇒ same MessageId ⇒ single delivery) and at L2 (Redis dispatch lock); the session processor distributes work across instances with zero coordination code (the broker grants each session lock to exactly one instance).

#### 4.2a Runtime & Deployment Topology — Option D (added v3.4 per DS-1b)

**Main site**: the L2 App Service is a **stock `DOTNETCORE|10.0` code-based deploy — no custom container**. Solution ZIPs, seed manifests, index schemas, and CI-pre-compiled ARM JSON travel as publish-payload content (~tens of MB). The main site contains **zero shells**: no pwsh, no az CLI, no pac. Every Azure/Graph/Dataverse/BAP operation runs through scoped SDK clients / `HttpClient` under `DefaultAzureCredential` pinned to the L2 UAMI — no ambient CLI auth sessions; failures surface as typed exceptions that map exactly onto the §4C taxonomy (this is what retires the stdout-parser silent-fail class the T1–T6 catalog exists to kill).

**EXO sidecar**: one **sitecontainer** on the SAME App Service (Linux sitecontainers, GA — same Plan, same UAMI, same App Insights; zero new Azure resources beyond an ACR repo tag; B2's parity rationale preserved). Image: `mcr.microsoft.com/powershell:7.4-mariner` + pinned `ExchangeOnlineManagement` module + `Set-ExchangeApplicationAccessPolicy.ps1` + a ~60-line HTTP listener. **≈200–230 MB compressed; ceiling 250 MB, Trivy-gated in CI.** Contract: `POST http://localhost:8091/apply-policy` `{tenantId, expectedAppIds[], policyScopeGroupId, correlationId: RunId, timeoutSeconds}` → `{outcome: Success|Failure|AlreadyCompliant, policiesApplied[], diagnostic}` — mirrors the script's existing `Write-ResultJson` envelope. The C# `ExchangePolicySidecarClient : IExchangePolicyApplier` maps the envelope onto `HandlerResult` exactly as the shell-out applier mapped exit codes. Auth: (main→sidecar) localhost-only binding + per-boot shared-secret header from platform KV; (sidecar→Exchange) app-only `Connect-ExchangeOnline` with the PFX fetched from platform KV at call time under the same UAMI, passed as `-Certificate` (X509 object — thumbprint mode assumes a Windows cert store a Linux container lacks). No new idempotency layer: the script is get-before-set idempotent; sidecar HTTP failures map connection-refused/timeout → `InfraFault` (Resumable), structured `Failure` → existing H14 classification. Observability: `correlationId = RunId` per request; one structured JSON log line per request → same Log Analytics workspace. Build: same GitHub Actions workflow as the main deploy, monthly rebuild cadence (pwsh + one signed module — a quiet loop, not az CLI's Python tree). Why this residual exists at all, and why it will live years: no Graph API exists for `ApplicationAccessPolicy` **or** its designated successor RBAC-for-Applications (both EXO-PowerShell-only, verified 2026-08-18 — DS-1b §0 with Microsoft Learn cites); the R22 migration is a sidecar-script change behind `IExchangePolicyApplier`, not a handler change.

**Rejected topologies** (DS-1b §3–4): fat tools container (Option A — ~1.5–2 GB, az CLI CVE stream, 25 stdout parsers, ambient auth sessions as permanent fleet infrastructure); ACA Job (reopens B2's Container-Apps rejection for one call/run); separate App Service (a second host for one cmdlet); ACI (cold start + separate identity story).

#### 4.2a.1 `.Api` / `.Worker` split contract (added by task 204d per DS-3 §3 Option 2 owner-lock)

The **single** `Sprk.Provisioning.ControlPlane` project referenced by v3.4 above no longer exists. Wave G-1 tasks 100 / 101 / 102 split it into three, driven by DS-3 §1.3's staging-slot shadow-worker defect finding — an always-on staging slot on a host running `StateReconcilerService` + `CrashRecoveryStartupService` + `ProvisioningHandlerDispatcher` would silently double-consume the production `sprk-provisioning-jobs` queue and double-write the production Cosmos `runs` container the instant it started. The split makes that defect **structurally impossible**, not merely gated.

**Project boundaries**:

| Project | Runtime role | Composition root | Staging slot? |
|---|---|---|---|
| `Sprk.Provisioning.ControlPlane.Api` | REST intake host. Serves `POST /api/runs`, `GET /api/runs/{id}`, and the 6 other `/api/runs/*` endpoints. Registers auth (JWT + Operator/Reader), Swagger, audit middleware, `IHandlerEnqueuer` (write side of the SB wire), `ICustomerRunGuard`, `IQuarantineClearService`, and shared Cosmos/SB/Telemetry clients. Zero `IProvisioningHandler` keyed registrations, zero `AddHostedService`. | `src/server/services/Sprk.Provisioning.ControlPlane.Api/Program.cs` | **Yes** — blue-green REST deploy via `modules/controlplane-app-service.bicep` (existing behavior). Safe because this host cannot execute a handler. |
| `Sprk.Provisioning.ControlPlane.Worker` | Background execution host. Registers all 21 keyed `IProvisioningHandler` implementations, `AddReconcilerModule` (→ `AddHostedService<StateReconcilerService>`), `AddHostedService<CrashRecoveryStartupService>`, and `AddHostedService<ProvisioningHandlerDispatcher>` (the `ServiceBusSessionProcessor` drain loop). Exposes only anonymous `/healthz` + `/ping`. | `src/server/services/Sprk.Provisioning.ControlPlane.Worker/Program.cs` | **No — slotless by Bicep design.** `modules/controlplane-worker-app-service.bicep` declares `Microsoft.Web/sites@2023-01-01` with no child `Microsoft.Web/sites/slots` resource; deploy = stop → zip-deploy → start. Crash-recovery + SB redelivery + §4C rollback machinery is the resume story (already exercised on every dispatcher restart). |
| `Sprk.Provisioning.ControlPlane.Core` | Shared types + module DI extension methods (`AddCosmosModule`, `AddServiceBusModule`, `AddReconcilerModule`, `AddCustomerRunGuard`, `AddRollbackModule`, `AddDispatchModule`, plus every `IProvisioningHandler` implementation and its collaborator seams). No composition root of its own. | (Class library) | N/A |

**Sizing**: both `.Api` and `.Worker` App Services run on the SAME P1v3 plan (per DS-3 §3 Option 2 — $0 marginal Azure cost). `.Worker` is `alwaysOn: true`.

**Test project references** (`Sprk.Provisioning.ControlPlane.Tests.csproj`): both `.Api` and `.Worker` are referenced; the `.Worker` reference carries `Aliases="WorkerHost"` so tests distinguish the two implicit `Program` top-level classes (`extern alias WorkerHost;` + `WorkerHost::Program` for the Worker-facing tests).

**Guard tests** (build-time invariant enforcement — regression closure of DS-3 §1.3):

| Test file | Invariant asserted |
|---|---|
| `Sprk.Provisioning.ControlPlane.Tests/Dispatch/HandlerRegistrationCompletenessTests.cs` | Every `HandlerIds.Dispatchable` id resolves to a keyed `IProvisioningHandler` in the **.Worker** DI graph (task 103, Wave G-1). |
| `Sprk.Provisioning.ControlPlane.Tests/Dispatch/ApiHostShadowWorkerGuardTests.cs` | The **.Api** DI graph registers ZERO project-owned `IHostedService` **and** ZERO keyed `IProvisioningHandler` for any `HandlerIds.Dispatchable` id (task 204d — this task). |
| `Sprk.Provisioning.ControlPlane.Tests/Dispatch/ProvisioningHandlerDispatcherInvariantTests.cs` | `MaxConcurrentCallsPerSession == 1` on the Worker's `ServiceBusSessionProcessorOptions` (task 102 forcing function; single-writer-per-customer). |

**What Path FLAGS would have looked like (rejected — see `notes/task-204d-path-decision.md`)**: three `Enabled` config flags (`Dispatcher:Enabled` / `Reconciler:Enabled` / `CrashRecovery:Enabled`) added to a single-host Worker composition, marked `slotSetting: true` in Bicep with production=true / staging=false. Post-split this reduces to pure new surface with no behavioral gain — the Worker has no staging slot to flip flags on, and reintroducing one would be the exact topology change the split was created to avoid.

#### 4.2b Dispatcher & Handler Resolution (added v3.4 per DS-2/DS-2b)

**`Dispatch/ProvisioningHandlerDispatcher`** — a `BackgroundService` hosting a `ServiceBusSessionProcessor` on `sprk-provisioning-jobs`:

```csharp
_processor = _serviceBusClient.CreateSessionProcessor(_queueName, new ServiceBusSessionProcessorOptions
{
    MaxConcurrentSessions        = _options.MaxConcurrentCustomers,   // config, default 4 — cross-customer parallelism
    MaxConcurrentCallsPerSession = 1,   // HARD-CODED — single-writer-per-customer correctness invariant (freeze test)
    SessionIdleTimeout           = TimeSpan.FromSeconds(30),          // gated runs release their session
    PrefetchCount                = 0,                                 // long handlers — no prefetch
    AutoCompleteMessages         = false,
    MaxAutoLockRenewalDuration   = TimeSpan.FromMinutes(65)           // H9/H6 pole
});
```

**Why session-serialized** (DS-2b, adversarially re-examined against 5 alternatives): every handler holds its run doc + ETag for a 10–60 min body and issues ONE terminal `ReplaceRunAsync` — under concurrent per-customer dispatch, every DAG branch-join becomes a systematic conflict generator that converts completed 30-min handlers into §4C re-dispatch churn. Sessions make handler∥handler races **structurally impossible** instead of survivable. The parallelism traded away is ~45–70 min of active compute on a ~27 h E2E (the 24 h SPE gate + consent gates dominate, and **gated runs hold no session**) ≈ 3–4% — and serialization is a per-customer latency policy with **zero fleet-throughput cost at any scale** (throughput = sessions × instances; multi-instance scale-out is broker-native). This is the single-writer-per-aggregate industry pattern (Orleans grain-per-key / Kafka partition-per-key / SB sessions). Flip condition + fallback recorded in §4.2 Concurrency (conditional-patch append).

**Handler resolution**: keyed DI by `HandlerId` (`"H0"`…`"H14"`) against `IProvisioningHandler` — the option the code itself anticipates (`IProvisioningHandler.cs` header; `HandlersModule.cs`). Divergences from the BFF's `ServiceBusJobProcessor` reference pattern (all deliberate, DS-2 §1.5): session processor (vs plain); keyed resolution (vs enumerate-and-match — instantiating 19 handler graphs per message is wasteful); 65-min lock renewal (vs 10); **retry authority is §4C `RollbackTransitions`** — the dispatcher completes the message once the outcome is *applied*; re-dispatch is a fresh enqueue with incremented `attempt`, never the SB Abandon/redeliver loop (which would double-retry against §4C). Level-2 idempotency (Redis dispatch lock) sits in the dequeue path; handlers own Level 3. Dead-letter policy mirrors the BFF's (`InvalidFormat` / `HandlerResolutionFailed` / `NoHandler` / `Poisoned` / `MaxRetriesExceeded`).

**Queue contract (IaC-declared, §11.2)**: `sprk-provisioning-jobs` with `requiresSession: true` + `requiresDuplicateDetection: true` (`duplicateDetectionHistoryTimeWindow: PT1H`) — both **create-time-only**; the pre-v3.4 live queue (az-CLI defaults: both OFF — sessions inert, dedup inert) MUST be deleted and recreated from the Bicep declaration (drain-verify first; namespace-scope RBAC survives). A session receiver on a non-session queue throws; a sessionful queue with a non-session receiver deadlocks — queue property and receiver type are one decision, taken together here.

**Forcing functions**: unit freeze-test on `MaxConcurrentCallsPerSession == 1`; contract test that L2 has no `IJobHandler` compile reference; runbook/deploy-script verification `az servicebus queue show --query "requiresSession,requiresDuplicateDetection"`.

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
| `POST /api/runs/{id}/clear-quarantine` | Operator | **NEW v3.2 (§4C)** — Explicitly clear a `Quarantined` run so a new run can start against the same customerId. Requires reason in body; audit-logged. |

### 4.3 Layer 3 — Swappable Front Ends (D16)

| Front end | Timeline | Character |
|-----------|----------|-----------|
| Claude Code operator skill (`/provision-environment`) | This project | Interactive; uses existing Dataverse MCP tools for data ops + skill handles sequencing/gates (like `/deploy-new-release`) |
| Fleet web app | Future | Lightweight read-only UI over Cosmos `runs` container; not MDA dashboard in r1 |
| Spaarke Assistant integration | Future | Natural-language provisioning via the same L2 API |

### 4.3a Claude Code Operator Toolchain (added v3.3 per Q4) — what Claude Code actually needs to execute

D16 says the L3 skill "calls L2 REST API" — but Claude Code is an agent, not a script; it needs concrete tools to actually execute each phase of a provisioning run. **This section enumerates the required toolchain, auth flow to L2, and the fallback matrix for when a tool is unavailable.**

#### 4.3a.1 Required tools (matrix)

| # | Capability | Primary tool | Fallback | Where it comes from |
|---|---|---|---|---|
| 1 | **Read design + spec + POML tasks** | Read / Glob / Grep | — | Native Claude Code tools |
| 2 | **Invoke PowerShell scripts** (H2a `Provision-Customer.ps1`, H6 `Deploy-DataverseSolutions.ps1`, etc.) | `PowerShell` tool | `Bash` tool with `pwsh -File` | Native |
| 3 | **Invoke Unix/bash tooling** (git, jq, curl fallbacks) | `Bash` tool | — | Native |
| 4 | **Call L2 REST API** (`POST /api/runs`, `GET /api/runs/{id}`, etc.) | `WebFetch` (with bearer token from step 5) OR `Bash` + `curl` + `az account get-access-token` | `PowerShell` + `Invoke-RestMethod` | Native + `az` CLI |
| 5 | **AAD bearer token for L2 auth** | `az account get-access-token --resource api://spaarke-provisioning-controlplane-{env}` | Interactive `az login` first, then step 5 | Requires `az` CLI installed on operator machine |
| 6 | **Read Dataverse** (gate verification queries — `systemusers`, `sprk_dataverseenvironment` reads) | Dataverse MCP tools (`mcp__dataverse__read_query`, `mcp__dataverse__search`) when connected | `pac data` PS commands OR raw Web API via `Invoke-RestMethod` with OAuth bearer | Dataverse MCP requires interactive OAuth once; PS fallback always available |
| 7 | **Write to Dataverse** (registry updates on run completion) | Dataverse MCP `mcp__dataverse__update_record` | `pac data` OR raw Web API PATCH | Same as #6 |
| 8 | **Read Azure resource state** (verify traps cleared, T1 keyVaultReferenceIdentity, resource existence checks) | `Bash` + `az ...` CLI (`az resource show`, `az keyvault ...`, `az webapp ...`) | Azure MCP if available | `az` CLI required |
| 9 | **Read Graph** (verify admin consent status, UAMI Graph role parity per T3) | `Bash` + `az ad ...` CLI OR `az rest` against Graph endpoints | Direct Graph SDK invocation via a script | `az` CLI |
| 10 | **Read Cosmos** (fleet status queries, run history) | `Bash` + `az cosmosdb sql query` CLI | `Invoke-RestMethod` with Cosmos SQL API | `az` CLI |
| 11 | **File upload to SPE** (H13 acceptance sample) | `Bash` + `curl` with Graph app-only token | `pnp` CLI OR Graph SDK script | `pnp` optional; Graph SDK always available |
| 12 | **Real-time run status polling** | `WebFetch` / `curl` loop against `GET /api/runs/{id}` (polling interval per H1 15–30s) | — | Native + step 4/5 tools |
| 13 | **Structured handoff report** (end-of-run) | `Write` (markdown to `projects/customer-provisioning-orchestration-r1/notes/runs/{runId}.md`) | — | Native |
| 14 | **Task tracking** (which handler is in-flight; which gate is pending) | `TodoWrite` tool | — | Native |
| 15 | **Multi-agent orchestration** for complex adversarial verification (rarely needed here) | `Agent` tool with subagent types (`researcher`, `general-purpose`) | — | Native; opt-in per invocation |

#### 4.3a.2 Auth flow to L2 REST API (B1 refinement)

The L2 REST API is AAD-protected (per §4.2 B1) with audience `api://spaarke-provisioning-controlplane-{env}` and requires `Operator` app-role for mutating calls (`POST /api/runs`, `POST /api/runs/{id}/resume`, etc.) or `Reader` for read-only (`GET /api/runs/{id}`).

**Claude Code's auth identity**: **the operator's own AAD identity** (interactive `az login`), NOT a service principal. This is deliberate — provisioning is auditable operator action, not automated. Operator's AAD identity must have `Operator` app-role assignment on the control-plane app-reg (single-tenant to Spaarke).

**Token acquisition** (Claude Code does this once per run, refreshes as needed):

```powershell
$token = az account get-access-token `
  --resource "api://spaarke-provisioning-controlplane-prod" `
  --query accessToken -o tsv

# All L2 calls use bearer token
Invoke-RestMethod `
  -Uri "https://spaarke-provisioning-prod.azurewebsites.net/api/runs" `
  -Method POST `
  -Headers @{ Authorization = "Bearer $token" } `
  -Body $payload -ContentType "application/json"
```

Token lifetime ~1 hour; auto-refresh via `az account get-access-token` again when 401s appear.

**Model 2 self-service exception** (D18 consent-callback): H0.5's `POST /api/onboarding/consent-callback` endpoint is anonymous with HMAC verification, NOT AAD-protected — this is by design because the customer admin is authenticating to their OWN tenant via the Microsoft consent flow, not to Spaarke's control plane. The HMAC signing key is a shared secret between the multi-tenant BFF app-reg's admin-consent redirect page and the L2 endpoint.

#### 4.3a.3 Operator machine prerequisites (MUST be installed before /provision-environment)

- **`pwsh`** ≥ 7.4 (PowerShell 7+; Claude Code invokes via `PowerShell` tool)
- **`az` CLI** ≥ 2.60 (latest per Aug 2026); operator must be logged in via `az login` before starting a run
- **`pac`** CLI ≥ 1.35 (Power Platform CLI; latest recommended)
- **`git`** ≥ 2.40 (for reading scripts + solution ZIPs from working tree)
- **Dataverse MCP** (optional but strongly recommended): configured via VS Code / Claude Code MCP settings; provides `mcp__dataverse__*` tools; interactive OAuth once per env
- **Azure MCP** (optional): if configured, provides higher-level Azure tools than raw `az` CLI

**Prerequisites-check step** (added to `/provision-environment` skill as Step 0):

Before any provisioning begins, the skill runs a self-check:

```powershell
# Verify tool versions
pwsh --version           # ≥ 7.4
az --version             # ≥ 2.60
pac --version            # ≥ 1.35

# Verify operator auth
az account show          # must show Spaarke tenant
az ad signed-in-user show # must show operator's principal

# Verify L2 API reachable + Operator role granted
az account get-access-token --resource api://spaarke-provisioning-controlplane-{env}
curl -H "Authorization: Bearer $token" https://spaarke-provisioning-{env}.azurewebsites.net/api/health

# Verify MCP if expected
mcp list                 # optional: confirm Dataverse MCP connected
```

Fail-fast with clear messages if any prereq missing.

#### 4.3a.4 Skill definition (Phase D deliverable, not yet written)

The `/provision-environment` skill lives at `.claude/skills/provision-environment/SKILL.md` and is created in **Phase D**. Structure (aligned with existing `/deploy-new-release` skill pattern):

1. **Purpose + trigger phrases** (`/provision-environment {customerId}`, "provision customer", etc.)
2. **Prerequisites** (per §4.3a.3)
3. **Interactive intake** (asks operator for `customerId`, `tenantId`, `tenancyModel`, `profile` if not passed as args)
4. **Preflight** — invokes L2 `POST /api/runs/{id}/preflight` = H0
5. **Confirmation gate** — shows operator the run plan + estimated cost + estimated duration; requires "yes" to proceed
6. **Execute** — loop: enqueue next handler → poll status until complete/failed → advance
7. **Manual gate handling** — if handler reaches `WaitingOnGate`, skill surfaces the gate + gates progression instruction (e.g., "operator: click admin consent URL then reply `advance`")
8. **Completion** — writes handoff report to `runs/{runId}.md`; updates `sprk_dataverseenvironment` registry via Dataverse MCP; produces final summary

**Cannot ship r1 without this skill.** Phase D delivers it.

#### 4.3a.5 Fallback matrix (when tools are unavailable)

| Failure | Impact | Fallback |
|---|---|---|
| Dataverse MCP disconnected mid-run | Cannot query gate state via MCP | Auto-switch to `pac data` or raw Web API PS calls; log the fallback; continue |
| Azure MCP unavailable | Cannot use higher-level Azure tools | Fall back to raw `az` CLI |
| Operator's `az` token expires mid-run | 401 on next L2 call | Auto-refresh via `az account get-access-token`; retry once; if still fails, pause + prompt operator |
| L2 API unreachable | Cannot advance run | Escalate immediately — pipeline halt; operator investigates; run auto-resumes via L2 crash-recovery (I6) when L2 back |
| PS script exits non-zero | Handler failed | Standard §4C rollback semantics kick in |
| Long-running handler times out (H2a Bicep 30 min, H5 dv-env creation 20 min) | Not a failure — expected | Skill polls L2 asynchronously; §4.2 handler execution model handles the actual work |

**Design implication**: the skill MUST be robust to MCP disconnects (they happen mid-session per our own experience — 2026-08-14, 2026-08-15). Fallback paths are mandatory, not optional.

---

## 5. ADR Constraint Analysis

### 5.1 ADR-004 — The Core Architectural Question

**The constraint**: All async work follows the ADR-004 job-contract shape — one message, one handler, one outcome (`IJobHandler` at the BFF; the L2-local `IProvisioningHandler` preserves the same shape). "MUST NOT use Durable Functions."

**The friction**: ADR-004 was designed for single-shot, stateless operations. Provisioning is multi-phase, stateful, gate-dependent orchestration.

**Resolution**: ADR-004 applies at two different levels:

| Level | Fits ADR-004? | Rationale |
|-------|--------------|-----------|
| **Individual handlers** (H0-H14) | Yes | Each is a self-contained operation. Individually, they match the existing 13 production handlers. |
| **Run orchestration** (sequencing, gates, state) | No — and shouldn't | This is the L2 control plane's job. It's a NEW component with its own patterns, not governed by ADR-004. |

**Design approach (Option A)**: Handlers implement the L2-local `IProvisioningHandler` (ADR-004-shaped; no BFF compile reference). The L2 control plane manages orchestration state and enqueues handlers. ADR-004 governs handler shape; the control plane builds a lightweight state machine (analogous to `Provision-Customer.ps1`'s state-file pattern, promoted to a proper run record).

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

### 5.4 What You'd Do Differently Without the ADRs — expanded v3.3 trade-off analysis

**Short answer**: nothing. ADR-004 (async job contract), ADR-010 (DI minimalism), ADR-017 (job status), Minimal API + ProblemDetails all correctly guide r1. The one place a reviewer might question is L2 orchestration — could we adopt Azure Durable Functions or Temporal instead of building a custom state machine over Cosmos? **The answer is still no**, and v3.3 makes the reasoning explicit rather than a footnote.

**Option A — custom state machine over Cosmos + `IProvisioningHandler` workers + L2 reconciler (what we chose)**:

| Pro | Con |
|---|---|
| Zero new infrastructure — reuses the ADR-004 *pattern* (job contract shape, Service Bus, Redis idempotency, Cosmos — all stack primitives) via the L2-local `IProvisioningHandler` contract | Must build state-reconciler `BackgroundService` + failure-taxonomy logic ourselves (~500–800 LOC net-new in L2) |
| Native fit for our shape — 19 heterogeneous handlers with per-handler idempotency keys, gates verifying external systems (Graph, ARM, Dataverse), and long human-in-the-loop gates (admin consent, SPE 24h replication) fit Cosmos-polling state better than a linear/graph workflow DSL | State-machine debugging is harder — no visualizer; must instrument logs to trace runs |
| Cost transparency — Cosmos serverless bills per-RU (predictable at provisioning cadence) | We own the retry/timeout/cancellation semantics — must get them right |
| Testability — each handler is a plain `IProvisioningHandler` unit-testable in isolation; L2 reconciler is a plain `BackgroundService` | State-advancement logic tested separately with Cosmos-fixture tests |
| No vendor lock — Cosmos + Service Bus are stack primitives; no dependency on a specific workflow product | Losing the "workflow product" advantage of built-in observability |
| Fits ADR-004 — no ADR change needed | ADR-004 was written for single-shot handlers; provisioning arguably at the edge of what ADR-004 anticipated |

**Option B — Azure Durable Functions (rejected)**:

| Pro | Con |
|---|---|
| Built-in orchestration DSL (`await context.CallActivityAsync(...)`) reads sequentially even though it's async | **Requires a Function App** — new Azure resource, new deployment story, new Bicep, new CI/CD; violates §4.2 B2 App-Service-parity decision |
| Free replay-based state (no explicit state store to design) | Replay model is subtle — accidentally non-deterministic code (`DateTime.Now`, random GUIDs, unbounded loops) breaks in production, not local |
| Automatic checkpointing + retry policies out of the box | Long-running orchestrations (>24h for SPE replication) hit Azure Storage limits on history table; requires "eternal orchestrations" pattern which is complex |
| Portal-based visualizer for instance debugging | **Adds a runtime dependency our stack currently doesn't have** — every future BFF/L2 change person must understand Durable Functions replay semantics |
| Strong for LINEAR workflows | Weaker for STATE-MACHINE workflows with human gates; the "wait for external event" pattern is inferior to Cosmos-polling ergonomically |

**Option C — Temporal (rejected)**:

| Pro | Con |
|---|---|
| Best-in-class workflow durability + human-gate semantics (`workflow.WaitForSignal`) | **Requires running a Temporal cluster** — either self-hosted (large ops burden) or Temporal Cloud (external vendor, network egress, cost) |
| Rich SDK + type-safe workflow authoring | Adds a whole new runtime, testing model, and mental model — team currently has zero Temporal expertise |
| Excellent observability + replay tooling | Cross-cloud dependency for what is otherwise an all-Azure stack |
| Solves problems we have (long orchestrations, human gates) | **Massive over-investment** for single-digit provisions per day |

**Long-term reality**: Provisioning cadence is **single-digit runs per day**. At that volume, the marginal cost of a custom state machine is invisible; the fixed cost of adopting a workflow product is huge. **Durable Functions makes sense when you have hundreds of concurrent orchestrations per hour** (e.g., order processing). **Temporal makes sense when you have thousands per hour + complex human-gate workflows** (e.g., loan approvals). Neither describes r1. The custom state machine (~500–800 LOC in L2) is a one-time cost; adopting a workflow product is an ongoing operational + cognitive tax.

**Migration story if we're wrong**: if provisioning cadence grows to hundreds/day in year 2+, migrating from custom Cosmos state machine to Durable Functions is possible — the handler contracts (`IProvisioningHandler`) don't change; only L2's orchestration layer changes. Reserved as an Option-D-future-work if cadence justifies. **v3.4 note**: within the current architecture, the sanctioned concurrency flip path is Cosmos conditional-patch append per §4.2 — a smaller step than any workflow-product migration, and equally deferred (no failing behavior today).

### 5.5 Inherited gates from r3-era master merge (added v3.2 per Fable M-6)

The 2026-08-15 merge of `origin/master` into this branch (commit `41bacbdae`) brought in r3-era forcing-functions + net10 cutover consequences that r1's new BFF code (H0.5 consent-callback endpoint, `DemoExpirationService` migration, `uami.bicep` Phase C work, extended `Validate-DeployedEnvironment.ps1`) MUST comply with. r3 handoff §6 details the full checklist; this section captures what applies to r1:

| Gate | Source | Applies to r1 how |
|---|---|---|
| **Analyzers-as-errors** (`TreatWarningsAsErrors=true` in `Directory.Build.props`; CS8601/CS8604 nullable are errors; CS0109/CS1998 removed) | r3 task 041 + net10 stricter analyzers | Every new r1 BFF code file must have zero warnings; H0.5 consent-callback endpoint + `DemoExpirationService` refactor cannot ship with any warning |
| **God-class ratchet** (`GodClassGuardTests`: no NEW `src/server` file > 2,000 LOC; 14 existing large files frozen at their LOC +100 grace) | r3 task 040 pattern | H0.5 consent-callback should be small (single controller + verification helper); `DemoExpirationService` refactor is a modification of an existing frozen file — must respect +100 LOC grace |
| **4 new ArchTests** (Dataverse downcast; ADR-013 `IActionResolver`/`IActionRunner` injection; layer violation) | r3 task 040 | H0.5 endpoint MUST NOT inject `IActionResolver`/`IActionRunner` directly (use `PublicContracts/` facade); MUST NOT downcast `IDataverseService` (use `UnwrapServiceClient` extension per r3 task 028) |
| **Config fail-fast at boot** (`ValidateDataAnnotations().ValidateOnStart()` on 24 Tier-1 IOptions classes) | r3 task 061 | If H0.5 adds any new `IOptions<T>` for onboarding config, it must be classified per Tier 1/2/3 (see `task-061-config-validation-classification.md`) and either validated-on-start (Tier 1) or added to the Tier-2 exemption list |
| **Publish size ≤60 MB compressed** (baseline 44.96 MB incl PDBs on net10) | CLAUDE.md §10 NFR-01 | H0.5 endpoint + Phase E migration + `uami.bicep` Phase C add ~0.1–0.3 MB combined; must report absolute + delta on every BFF-touching task per §17 placement justification |
| **Zero HIGH CVE** (`dotnet list --vulnerable --include-transitive` = zero, current net10 baseline) | r3 task 032 + net10 cutover | H0.5 must not introduce any new NuGet package; `uami.bicep` is IaC-only (no NuGet impact); r3 owns the deferred-package-majors backlog (#772) — do not touch |
| **Graph v6 / Kiota 2.0 error type** (`ODataError` not `ServiceException`; `ResponseStatusCode` is int; `ResponseHeaders` is dict) | net10 cutover r3-handback | H10 Graph SDK calls (Graph role parity via `GraphAppRoles.cs`) must catch `ODataError` in any new code |
| **Naming-conformance gate** (`scripts/naming-conformance-check.ps1` advisory-until-remediated) | r3 task 063 | H4 canonical-secret population + Phase G naming remediation must produce a clean pass per §7.9 |

**Enforcement**: These gates run inside `dotnet test tests/Spaarke.ArchTests` (already in the tier-1 blocking CI job). r1's every BFF PR must pass. `/conflict-check` before every BFF PR per r3 handoff §7 (19 active BFF worktrees at present).

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

**Serialization contract (v3.4 — C4.5 / bug #19/#20 family)**: the Cosmos client uses the SDK **default (Newtonsoft) serializer** with camelCase policy; STJ attributes are ignored on the write path. Therefore, on the run-document POCO graph: (1) `RunStatus`, `GateState`, `QuarantineState` carry **dual converters** — STJ `JsonStringEnumConverter` AND `[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]` — so `status` is written as a string and `CosmosActiveRunScanner`'s `WHERE c.status IN ('Running','WaitingOnGate')` matches (without this, the reconciler and I6 crash recovery scan zero rows forever — a working dispatcher looks hung); (2) `RunId` carries dual `id` attributes; (3) no `Ttl` property (Cosmos rejects `"ttl": null`; if TTL returns, it must be Newtonsoft-visible with `NullValueHandling.Ignore`). Guarded by the serializer-contract unit test + the repository→scanner integration seam test (`tests/integration/seam/**` — the test class that would have caught this). Misleading comments at `CosmosModule.cs:140` and `CosmosActiveRunScanner.cs:40–44` corrected to state the real mechanism.

**Fleet visibility**: Future web app reads from Cosmos directly. No Dataverse sync needed in r1 — the `sprk_dataverseenvironment` entity provides fleet-level status via `Setup Status` field (already deployed) + `sprk_currentrunid` for in-flight runs.

---

## 7. Azure Resource Specification (Per-Customer)

Every Model 2 customer environment deploys a dedicated, isolated set of Azure resources per D3. Model 1 (trial/SMB per §3A A1) deploys the same resources except the three fixed-floor items (App Service Plan, OpenAI, AI Search) share the Spaarke platform tier. **Redis handling is model-specific as of v3.6 (task 128b, E2 reconciliation)**: Model 1 — Redis remains per-environment (shared across customers within an env) per Q-E FR-12, deployed via `scripts/Deploy-RedisCache.ps1`, NOT per-customer (v3.2 M-1 correction, unchanged for Model 1). Model 2 — Redis is **per-customer**, deployed via `customer.bicep`'s `modules/redis.bicep` invocation, because `customer.bicep` is the sole template deployed for the Model2Dedicated branch where env=customer 1:1 — "per-environment" and "per-customer" collapse to the same unit for this template. Reverses v3.2 for Model 2 only; owner-confirmed 2026-08-19.

> **v3 note**: [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) §7 is the **authoritative** BOM (with per-resource disposition, RBAC provisioning steps, and shared-vs-dedicated matrix). This section captures the *design* decisions — naming, catalog, and deployment order — that H2a's Bicep composition depends on. When the two disagree, INVENTORY wins.

**v3 additions to the v2 catalog**:
- **Cosmos DB (serverless)** — required by BFF runtime (AI sessions, prompts, audit, memory, feedback) per INVENTORY §7. v2 omitted this; BFF will not start without it. Partition by `/tenantId`. **Per-customer in both Model 1 and Model 2** (v3.2 correction: Model 1 also gets a dedicated Cosmos account for runtime data isolation — only fixed-floor levers per §3A A1 are shared, and Cosmos serverless has no fixed floor).
- **SignalR (optional / Null-Object)** — notifications spine realtime per ADR-034. Feature-gated; deploys only if `Notifications:SignalRSpine:Enabled=true`.
- **Two model stacks made first-class** — `model1-shared.bicep` (trial tier) alongside `model2-full.bicep` (dedicated). Not stack drift; deliberate composition per §3A A1.

**v3.2 corrections to v3**:
- **Redis REMOVED from per-customer stamp** (Q-E FR-12; `Provision-Customer.ps1` step 3 header comment authoritative). Redis is a per-environment shared resource; H2a does NOT provision Redis; BFF app-settings still reference the per-env Redis via canonical KV secret.
- **UAMI is aspirational, not yet built** — v3.1 asserted UAMI provisioned by `uami.bicep`; that module does NOT exist and `app-service.bicep` currently uses System-Assigned MI (`identity: enableManagedIdentity`). **Phase C** adds new `uami.bicep` module + refactors `app-service.bicep` to consume UAMI + bind to both slots (structural fix for T5). Until Phase C lands, H4 grants KV RBAC to both slots' distinct System-Assigned MI principals (interim T5 mitigation).
- **`cosmos.bicep` module name corrected** — actual filename is `cosmos-db.bicep`.
- **Bicep module count** — 25 `.bicep` modules (v3.1 said 26; discrepancy was a `.json` lifecycle policy counted as a module).

### 7.1 Resource Naming Convention

| Resource Type | Pattern | Example (`customerId=acme`, `env=prod`) |
|---|---|---|
| Resource Group | `rg-spaarke-{customerId}-{env}` | `rg-spaarke-acme-prod` |
| **UAMI — Model 1 (shared)** (added 2026-08-26 per Q8 owner disposition / task 205d A41 — §6.5 resolution record + FR-40 I6) | `sprk-{env}-shared-bff-uami` | `sprk-prod-shared-bff-uami` |
| **UAMI — Model 2 (dedicated)** (v3.2 Phase C) | `mi-spaarke-{customerId}-{env}` | `mi-spaarke-acme-prod` |
| Storage Account | `sprk{customerId}{env}sa` | `sprkacmeprodsa` |
| Key Vault (canonical per r3 task 063) | `sprk-{env}-kv` (Model 1 shared) OR `sprk-{customerId}-{env}-kv` (Model 2 dedicated); dev exception: `spaarke-spekvcert` (DO-NOT-RENAME) | Model 1: `sprk-prod-kv`; Model 2: `sprk-acme-prod-kv`; Dev: `spaarke-spekvcert` |
| Service Bus | `spaarke-{customerId}-{env}-sbus` | `spaarke-acme-prod-sbus` |
| Redis Cache (v3.6 reinstated, task 128b — **Model 2 only**; Model 1 remains per-env, not per-customer, per Q-E FR-12) | `sprk-{customerId}-{env}-redis` | `sprk-acme-prod-redis` |
| Cosmos DB (v3.2 added) | `cosmos-spaarke-{customerId}-{env}` | `cosmos-spaarke-acme-prod` |
| App Service Plan | `sprk-{customerId}-{env}-plan` | `sprk-acme-prod-plan` |
| App Service (BFF) | `sprk-{customerId}-{env}-api` | `sprk-acme-prod-api` |
| OpenAI | `sprk-{customerId}-{env}-openai` | `sprk-acme-prod-openai` |
| AI Search | `sprk-{customerId}-{env}-search` | `sprk-acme-prod-search` |
| Document Intelligence | `sprk-{customerId}-{env}-docintel` | `sprk-acme-prod-docintel` |
| App Insights | `sprk-{customerId}-{env}-insights` | `sprk-acme-prod-insights` |
| Log Analytics | `sprk-{customerId}-{env}-logs` | `sprk-acme-prod-logs` |
| SignalR (optional) | `sprk-{customerId}-{env}-signalr` | `sprk-acme-prod-signalr` |

### 7.2 Resource Catalog

| # | Resource | Bicep Module | Default SKU | Key Configuration |
|---|----------|-------------|-------------|-------------------|
| 1 | **Resource Group** | (subscription-level) | — | Tags: customer, environment, application, managedBy |
| 2 | **User-Assigned Managed Identity** *(v3.2 target — new `uami.bicep` module in Phase C)* | `uami.bicep` *(NEW — does not exist yet; Phase C creates)* | — | Server-outbound identity (Graph app-only, Dataverse, Cosmos, KV). See §9.2 for RBAC + Graph roles. **INVENTORY §7 T1/T2/T3/T5 traps apply.** **Interim state (pre-Phase C)**: `app-service.bicep` uses System-Assigned MI; H4 grants KV RBAC to both slots. |
| 3 | **Key Vault** | `key-vault.bicep` | Standard | RBAC auth, soft delete 90d, purge protection, UAMI gets Secrets User role. **App Service `keyVaultReferenceIdentity` PATCHed to UAMI** (silent-fail T1). **v3.2 Phase G**: vault name is `sprk-{env}-kv` per canonical naming standard (`spaarke-spekvcert` DO-NOT-RENAME dev exception codified); bicep accepts vault name as a **parameter**, not hardcoded. |
| 4 | **Storage Account** | `storage-account.bicep` | Standard_LRS | TLS 1.2, blob public access disabled, 3 containers (see 7.3) |
| 5 | **Service Bus** | `service-bus.bicep` | Standard | TLS 1.2, 4 queues + 1 membership topic (see 7.3), 5-min lock, 14-day TTL, DLQ enabled |
| ~~6~~ | ~~**Redis Cache**~~ | — | — | **REMOVED v3.2 for MODEL 1 ONLY (M-1 / Q-E FR-12)**: Model 1 Redis is per-environment (shared across customers within an env), NOT per-customer. Deployed via `scripts/Deploy-RedisCache.ps1` at env-provisioning time; H2a does not touch Redis for Model 1; BFF app-settings reference the per-env Redis via canonical KV secret. |
| 6b | **Redis Cache** *(v3.6 REINSTATED for MODEL 2 ONLY — task 128b, E2 reconciliation)* | `redis.bicep` | Basic / C0 | `customer.bicep`-provisioned, unconditional invocation (no feature gate) — `customer.bicep` is the sole Model2Dedicated template, env=customer 1:1, so per-environment Redis IS per-customer Redis here. `allkeys-lru` eviction, TLS 1.2 min (FR-09 hardened module). No UAMI RBAC (access-key auth). Model 1 is unaffected — see row 6 above. |
| 6 | **Cosmos DB (serverless)** *(v3 added)* | `cosmos-db.bicep` *(v3.2 name corrected)* | Serverless | AI sessions, prompts, audit, memory, feedback; partition `/tenantId`; UAMI granted **Cosmos DB Built-in Data Contributor**. **BFF will not start without this.** |
| 7 | **App Service Plan** | `app-service-plan.bicep` | S1 (Standard) | Linux |
| 8 | **App Service (BFF)** | `app-service.bicep` | — | **.NET 10.0** (v3.2 net10 baseline per 2026-08-14 cutover), HTTPS-only, always-on, HTTP/2, UAMI (post-Phase C) or System-Assigned MI (interim), health check `/health`. **Staging slot MI parity** (silent-fail T5 — structurally fixed by Phase C UAMI). |
| 9 | **Azure OpenAI** | `openai.bicep` | S0 (`kind=AIServices`) | 4 model deployments (see 7.4). UAMI granted **Cognitive Services User** (wildcard; narrower OpenAI-User role insufficient for `kind=AIServices`). |
| 10 | **AI Search** | `ai-search.bicep` | Standard | Semantic search enabled, **7 canonical indexes** (see Section 8; index creation is handler **H2b** via `scripts/ai-search/Deploy-AllIndexes.ps1` per FR-07 — v3.2 path correction — not Bicep) |
| 11 | **Document Intelligence** | `doc-intelligence.bicep` | S0 | prebuilt-layout model (see 7.5) |
| 12 | **App Insights + Log Analytics** | `monitoring.bicep` | PerGB2018 | 90-day retention, resource permissions enabled |
| 13 | **SignalR** (optional / Null-Object) *(v3 added)* | `signalr.bicep` | Free F1 / Standard S1 | Notifications spine realtime per ADR-034. Feature-gated (`Notifications:SignalRSpine:Enabled`). |
| 14 | **Content Safety** (optional) | `content-safety.bicep` | S0 | West US 2 or East US 2 only (Prompt Shields requirement) |
| 15 | **AI Foundry Hub + Project** (optional) | `ai-foundry-hub.bicep` | Basic | Prompt Flow orchestration, attached to storage + KV + insights |

**Shared-vs-dedicated disposition** (per §3A A1 + INVENTORY §11 + v3.2 Redis correction):

| Category | Resources | Model 1 (trial) | Model 2 (dedicated, D3) |
|---|---|---|---|
| 🔴 Always dedicated (cheap / customer-owned) | Dataverse, SPE, KV secrets, Storage, UAMI, Entra app config, **Cosmos runtime data (v3.2 correction: dedicated in both models)** | dedicated | dedicated |
| 🟡 Fixed-floor levers (§3A A1 amendment) | App Service Plan, Azure OpenAI, AI Search | **shared** (metered per D19) | dedicated |
| 🟢 Safely shareable | Service Bus, App Insights/Log Analytics, Content Safety, Doc Intelligence, SignalR | shared | dedicated |
| 🔵 Per-environment (Model 1) / 🟢 Dedicated per-customer (Model 2) — **split v3.6, task 128b E2 reconciliation** | Redis Cache — Model 1: per Q-E FR-12, `scripts/Deploy-RedisCache.ps1` deploys once per env, all customers within that env share (v3.2, unchanged). Model 2: `customer.bicep`-provisioned via `redis.bicep` (v3.6 reinstated) — env=customer 1:1 in this template, so per-environment collapses to per-customer | shared | dedicated per-customer |

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

### 7.6 Deployment Order (v3.2 updated)

1. Resource Group
2. **UAMI** *(v3.2 Phase C target — new `uami.bicep`; interim uses System-Assigned MI at step 13)*
3. Log Analytics + App Insights (monitoring, referenced by others)
4. Key Vault (secrets storage, created early so other modules can store outputs; UAMI → Secrets User; **v3.2 Phase G: vault name is a parameter, canonical form `sprk-{env}-kv` per r3 task 063**)
5. Storage Account (UAMI → Blob Data Contributor)
6. Service Bus
7. **Cosmos DB (serverless)** — BFF prereq; UAMI → Data Contributor
8. ~~Redis Cache — REMOVED v3.2 (Q-E FR-12) — for MODEL 1 ONLY: deployed per-env via `scripts/Deploy-RedisCache.ps1`, not per-customer~~. **v3.6 (task 128b, E2 reconciliation): for MODEL 2, Redis Cache IS deployed here** (`redis.bicep`, unconditional invocation, `Basic`/`C0` default) — placed after Document Intelligence (step 12) in the actual `customer.bicep` insertion order (task 128b groups supporting-infra together; this numbered list predates that grouping and is not meant to imply strict sequential dependency between steps 3-14).
9. App Service Plan
10. OpenAI Service (`kind=AIServices`; UAMI → Cognitive Services User)
11. AI Search (**index creation is H2b via `scripts/ai-search/Deploy-AllIndexes.ps1`, not part of this Bicep phase**)
12. Document Intelligence
13. **SignalR** (optional)
14. App Service (BFF, .NET 10, depends on plan + KV + all AI/data service endpoints; **`keyVaultReferenceIdentity` PATCHed to UAMI as post-deploy step per T1**)
15. Content Safety (optional)
16. AI Foundry Hub + Project (optional)

**Then, after Bicep completes** (post-H2a): H2b (7 canonical AI Search indexes via `scripts/ai-search/Deploy-AllIndexes.ps1`), H4 (KV secrets population via canonical secret-catalog manifest per Phase H + `keyVaultReferenceIdentity` PATCH per T1), then H3 onward per DAG.

**Note (task 128b, 2026-08-19)**: this numbered list is the design-time intent; the actual `customer.bicep` module-insertion order (as landed by tasks 127/128/128b) groups new sections at the point in the file each task's declared insertion zone allowed, to avoid disturbing already-shipped modules — it is not a strict re-ordering of the file to match this list exactly. Concretely: Monitoring (App Insights + Log Analytics) is placed after Key Vault / before Storage Account (not before Key Vault as step 3 implies); Document Intelligence is placed after AI Search / before Membership Topic (step 12, matching); Redis is placed immediately after Document Intelligence / before Membership Topic (near step 8's numbered position but physically adjacent to step 12 in the file). Functional deployment order (Bicep's own dependency graph) is unaffected — Bicep resolves actual dependencies via `dependsOn`/output references, not file line order.

### 7.7 Key Vault Secrets (Populated by H4 — canonical names per r3 task 063 standard)

**v3.2 changes**:
- Names comply with the canonical naming standard (R1: env-agnostic; R2: one canonical casing; R3: `sprk-{env}-kv` vault; §7.9)
- **Dataverse-S2S-* secrets DROPPED** — r3 task 060 dropped the vestigial S2S app-reg; secrets have zero consumers
- **`redis-connection-string`** — **model-specific as of v3.6 (task 128b, E2 reconciliation)**: **Model 1** — DROPPED (unchanged from v3.2); Redis is per-env, BFF app-settings reference the per-env Redis secret in the platform KV, not per-customer. **Model 2** — RESTORED as a per-customer KV entry, sourced from `customer.bicep`'s `redis.bicep` module output (`redis.outputs.redisConnectionString`, embeds the access key) — wiring this into `kv-secrets.generated.bicep`'s `secretValues` object is task 129's mandate (out of scope for task 128b itself, which only makes the underlying module output available as an in-file symbolic reference).
- Secrets are seeded from a **canonical secret-catalog manifest** (Phase H per r3 KV federation design Phase 3b) — one authoritative source generates seeder + Configure script + tokens doc + Bicep KV secret set

**Infrastructure secrets (from Bicep outputs):**

| Secret Name (canonical) | Source | Purpose |
|-------------|--------|---------|
| `servicebus-connection-string` | Service Bus deployment output | Queue access |
| `storage-connection-string` | Storage deployment output | Blob access |
| `AiSearch--AdminKey` *(v3.2 canonical per r3 task 063 — was `aisearch-admin-key` + 2 aliases)* | AI Search deployment output | Index management |
| `ai-search-endpoint` | AI Search deployment output | Search endpoint |
| `openai-api-key` | OpenAI deployment output | AI model access (fallback when MI auth unavailable per ADR-028 E-2) |
| `ai-openai-endpoint` | OpenAI deployment output | AI model endpoint |
| `ai-docintel-endpoint` | Doc Intelligence deployment output | Document processing endpoint |
| `ai-docintel-key` | Doc Intelligence deployment output | Document processing access |
| `AppInsights-ConnectionString` | App Insights deployment output | Telemetry |
| `cosmos-endpoint` *(v3.2 added)* | Cosmos DB deployment output | BFF Cosmos connection endpoint |

**Auth secrets (from H3 Entra app registration):**

| Secret Name (canonical) | Source | Purpose |
|-------------|--------|---------|
| `BFF-API-ClientId` | App registration | BFF app registration client ID |
| `BFF-API-ClientSecret` | App registration credential | ~~OBO flow client secret (24-month expiry) — **NEVER-REMOVE** per r3 handoff (BFF's Dataverse camp still secret-based pending task 011 #3b)~~ **Superseded 2026-08-25 per §6.5 resolution**: DELETED from KV 2026-08-24 (auth-v4 task 033, E-3 CLOSED). See spec.md MUST + `.claude/constraints/provisioning.md` §KV credential lifecycle. |
| `BFF-API-Audience` | `api://{bff-app-id}` | JWT audience validation |
| `Dataverse-ServiceUrl` *(v3.2 canonical per r3 task 063 — was `SPRK-DEV-DATAVERSE-URL` with env token baked in)* | Dataverse environment | Cross-reference; env token lives in the value + vault, never the name |
| ~~`Dataverse-S2S-ClientId`~~ | ~~S2S app registration~~ | **DROPPED v3.2 (r3 task 060)** — vestigial S2S app-reg had zero code consumers |
| ~~`Dataverse-S2S-ClientSecret`~~ | ~~S2S app registration credential~~ | **DROPPED v3.2 (r3 task 060)** |
| `TenantId` | Customer Entra tenant | MSAL authority |

**Integration secrets:**

| Secret Name (canonical) | Source | Purpose |
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

### 7.9 KV-Secret & Resource Naming Compliance (added v3.2 per r3 task 063 handoff §4a)

r3 published the canonical naming standard in [`docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` §"KV-Secret & Resource Naming Standard (Conformance-Gated)"](../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md#kv-secret--resource-naming-standard-conformance-gated) and the read-only gate at `scripts/naming-conformance-check.ps1`. **r1 owns applying canonical names at provisioning time** (Phase G) + is the driver for the durable fix (Phase H canonical secret-catalog manifest).

**The four load-bearing rules (r3 authoritative)**:

1. **R1 — Env-agnostic secret names**: no `DEV/DEMO/PROD/UAT/TEST/STAGING/SANDBOX/QA` token as a delimited segment of a KV-secret name. Env difference lives in the value + vault, never the name.
2. **R2 — One canonical casing per logical secret**: kebab-case for new secrets (`communication-webhook-signing-key`); existing PascalCase live secrets grandfathered but MUST NOT gain a second casing. Never two casings for one value.
3. **R3 — Canonical vault name**: `sprk-{env}-kv`. **Codified legacy exception (DO-NOT-RENAME)**: `spaarke-spekvcert` — the only live dev vault; bicep accepts vault name as a **parameter** rather than hardcoding.
4. **No orphan / duplicate secrets** — every secret the template/app reads is provisioned by the seeder under exactly the canonical name.

**Reference syntax** (single form): `@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=<Canonical-Name>)`. Do not mix `#{KEY_VAULT_NAME}#` / SecretUri token schemes for the same value.

**r1 remediation obligations per r3 task 063 handoff §4a rename map**:

| Current (drift) | Canonical | r1 action |
|---|---|---|
| `SPRK-DEV-DATAVERSE-URL` (env-token-in-name) | `Dataverse-ServiceUrl` | Bake canonical into H4 seed; Bicep param the vault name |
| AI-Search key: 3 aliases / 3 casings | `AiSearch--AdminKey` | ⚠ Dataverse + live App-Service pre-check FIRST before removing aliases |
| `BFF-API-ClientSecret` vs `bff-api-client-secret` (Office add-in path) | single canonical casing `BFF-API-ClientSecret` | grandfather ONE casing; H4 only ever writes canonical |
| Vault `sprk-platform-prod-kv`, `kv-sdap-{env}`, `spaarke-kv-dev`, `sprkshareddev-kv` | `sprk-{env}-kv` + codified `spaarke-spekvcert` dev exception | make bicep vault name a PARAM; do NOT recreate live vault (dev is authoritative) |
| platform.bicep flat keys `openai-api-key`/`aisearch-admin-key`/`docintel-key` (0 code binds) | canonical names + `__` app-setting keys, or delete redundant settings | H2a bicep alignment; delete orphan flat keys |
| 6 template-referenced secrets never seeded (orphan refs) | Add to canonical secret-catalog manifest (Phase H) OR document out-of-band | Phase H manifest closure |
| Webhook secrets: `communication-webhook-signing-key` vs `compose-webhook-signingkey` (inconsistent separation) | consistent kebab separation | env-coordinate; prod currently decommissioned → cheap window |

**BINDING pre-check** (per r3 task 063 handoff): before removing any alias/fallback spelling, pre-check LIVE App Service settings + KV + Dataverse-persisted config. ~~**Never delete** `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (OBO + shared-lib Dataverse still depend on them; #3b credential migration is task 011, not r1).~~ **Superseded 2026-08-25 per §6.5 resolution**: `BFF-API-ClientSecret` DELETED from KV 2026-08-24 (E-3 CLOSED); `Dataverse-ClientSecret` retained through 2026-11-23 as auth-v4's rollback copy. Full rule: spec.md MUST + `.claude/constraints/provisioning.md` §KV credential lifecycle.

**Owner directive #3 (2026-08-15) applied**: r1 does NOT fix live dev drift as a maintenance-window activity. r1 DOES bake canonical naming into all provisioning paths (H4, Bicep param-ization, H2a modules) so that **new** customer environments are compliant from day one. This satisfies "accurate KV setup to support Model 1 and Model 2 approach" without spending cycles on dev remediation for an env that works.

**Verification**: `scripts/naming-conformance-check.ps1` runs advisory-until-remediated per surface. Success criterion #17 (§15) requires exit 0 on r1-owned surfaces post-provisioning.

---

## 8. AI Search Index Specification

> **v3 note (2026-08-12)**: [`COMPONENT-INVENTORY.md`](COMPONENT-INVENTORY.md) §9 references the current 7-index catalog and its deployment script (`scripts/ai-search/Deploy-AllIndexes.ps1`). This section captures the *design* — naming standard + field-level shape (audit-reference, may drift from JSON schemas; Phase A verifies). Handler **H2b** invokes `Deploy-AllIndexes.ps1` after H2a Bicep completes.

### 8.1 Index Naming Standard

**Convention**: `spaarke-{subject}-{qualifier}` where `{subject}` identifies the data domain and `{qualifier}` distinguishes index variants when needed.

### 8.2 Active Index Inventory (7 canonical indexes per FR-07) — v3.2 rewritten from Deploy-AllIndexes.ps1 catalog

**Authoritative source (v3.2)**: `scripts/ai-search/Deploy-AllIndexes.ps1` catalog (lines 197–264) + `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` §4. This section MIRRORS that catalog; the script is the source of truth. All indexes use **3072-dimensional vectors** with `text-embedding-3-large`, HNSW algorithm (m=4, efConstruction=400, efSearch=500, cosine metric), and semantic ranking.

**v3.2 corrections vs v3.1** (per Fable H-2):
- `spaarke-file-index` → **`spaarke-files-index`** (plural; matches catalog + JSON filename)
- `spaarke-discovery-index` is **ACTIVE** in the catalog (v3.1 wrongly said "dropped")
- `spaarke-playbook-embeddings` **RETIRED** per spaarke-ai-architecture-redesign-r1 task 035 / FR-P2-06 (dispatcher stack retirement — noted explicitly in Deploy-AllIndexes.ps1 header)
- `spaarke-knowledge-index` **RETIRED / archived** — JSON only under `infrastructure/ai-search/_archive/`; not in the deployer catalog
- All Schema Location paths corrected to `infrastructure/ai-search/*.json` (verified on disk)

| # | Index Name (canonical) | Purpose | Vector Fields | Tenant Isolation | Schema Location |
|---|-----------|---------|--------|-----------------|-----------------|
| 1 | `spaarke-files-index` | Chunked document content from SPE files | `contentVector3072` + `documentVector3072` | `tenantId` + `privilege_group_ids` filter | `infrastructure/ai-search/spaarke-files-index.json` |
| 2 | `spaarke-discovery-index` | Discovery-workflow document indexing | `contentVector3072` + `documentVector3072` | `tenantId` + `privilege_group_ids` filter | `infrastructure/ai-search/spaarke-discovery-index.json` |
| 3 | `spaarke-records-index` | Dataverse entity records (Matter, Project, Invoice, etc.) | `contentVector` | `tenantId` + `recordType` + `dataverseRecordId` + `dataverseEntityName` + `privilege_group_ids` | `infrastructure/ai-search/spaarke-records-index.json` |
| 4 | `spaarke-rag-references` | Golden reference knowledge (curated enterprise knowledge; **FR-17 semantic config → `documentType`, NOT `domain`**) | `contentVector3072` | `tenantId` + `documentType` + `knowledgeSourceId` | `infrastructure/ai-search/spaarke-rag-references.json` |
| 5 | `spaarke-insights-index` | Observations and Precedents (discriminated by `artifactType`) | `contentVector` | `tenantId` + `artifactType` filter | `infrastructure/ai-search/spaarke-insights-index.json` |
| 6 | `spaarke-session-files` | Session-scoped chat uploads (per ADR-014 — strict per-session tenant isolation) | `contentVector3072` + `documentVector3072` | `tenantId` + `sessionId` (dual filter — canonical ADR-014 invariant) | `infrastructure/ai-search/spaarke-session-files.json` |
| 7 | `spaarke-invoices-index` | Invoice chunks for financial analysis | `contentVector` | `tenantId` + `invoiceId` + `matterId` + `projectId` | `infrastructure/ai-search/spaarke-invoices-index.json` |

**Retired / archived (do NOT provision — v3.2 accurate list)**:
- `spaarke-playbook-embeddings` — retired by spaarke-ai-architecture-redesign-r1 task 035 / FR-P2-06 with dispatcher stack
- `spaarke-knowledge-index` — archived under `infrastructure/ai-search/_archive/`; superseded by `spaarke-files-index` + `spaarke-rag-references` split
- `spaarke-knowledge-index-v2` (dual-vector 1536+3072) — never went active

**Per-index invariant verification**: `Deploy-AllIndexes.ps1` verifier (NFR-02) asserts required filterable fields + vector field presence + forbidden field absence (e.g., no `domain` field on `spaarke-rag-references` per FR-17). H2b runs the deployer + verifier; H13 acceptance re-runs verifier as a smoke test.

### 8.3 Index Field Specifications

**v3.2 note**: field-level schemas live in the JSON files under `infrastructure/ai-search/` — those are the source of truth deployed by H2b via `Deploy-AllIndexes.ps1`. The tables below are the v2 audit-reference snapshot for the 5 indexes that remain in the canonical set (`spaarke-files-index`, `spaarke-insights-index`, `spaarke-invoices-index`, `spaarke-rag-references`, `spaarke-records-index`, `spaarke-session-files`). **v3.2 explicit corrections**:
- The `spaarke-file-index` section below is actually the audit for `spaarke-files-index` (plural) — the singular v3.1 naming was wrong
- **DO NOT reference** the `spaarke-playbook-embeddings` field table below OR the `spaarke-knowledge-index` field table below — both retired/archived per §8.2

A field-by-field diff against the current JSON schemas + `Deploy-AllIndexes.ps1` catalog invariants is a Phase A verification item (per INVENTORY §12 + Fable H-2). The verified catalog invariants (required filterable fields, vector fields, forbidden fields) live in the Deploy script — treat those as binding, not the tables below.

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

### 8.4 Index Configuration (AiSearchOptions) — v3.2 reconciled

BFF configuration maps (`src/server/api/Sprk.Bff.Api/Configuration/AiSearchOptions.cs`) — reconciled against `Deploy-AllIndexes.ps1` catalog:

| Config Key | Index Name (canonical) | Notes |
|-----------|-----------|-------|
| `AiSearch:FilesIndexName` | `spaarke-files-index` *(v3.2 plural)* | Primary document search |
| `AiSearch:InsightsIndexName` | `spaarke-insights-index` | Observations + Precedents |
| `AiSearch:RagReferencesIndexName` | `spaarke-rag-references` | Golden references (FR-17: `documentType` not `domain`) |
| `AiSearch:SessionFilesIndexName` | `spaarke-session-files` | Session-scoped uploads (ADR-014 dual filter) |
| `AiSearch:RecordsIndexName` | `spaarke-records-index` | Dataverse entity records |
| `AiSearch:InvoicesIndexName` | `spaarke-invoices-index` | Invoice chunks |
| `AiSearch:DiscoveryIndexName` | `spaarke-discovery-index` *(v3.2 ACTIVE — v3.1 wrongly marked deprecated)* | Discovery workflow indexing |
| ~~`AiSearch:KnowledgeIndexName`~~ | ~~`spaarke-knowledge-index`~~ | **RETIRED v3.2** — archived under `_archive/`; superseded by files-index + rag-references split |
| ~~(playbook-embeddings)~~ | ~~`spaarke-playbook-embeddings`~~ | **RETIRED v3.2** — dispatcher-stack retirement per spaarke-ai-architecture-redesign-r1 task 035 / FR-P2-06 |
| `AiSearch:AllowedIndexes` | Operator-configured allow-list | Per-environment index access |

**Phase A audit**: reconcile `AiSearchOptions.cs` binding names against the 7 canonical index names above. Retire the `KnowledgeIndexName` binding + any code paths that reference it.

### 8.5 Index Provisioning — **Handler H2b (v3.2 path corrected)**

After H2a Bicep completes, handler **H2b** invokes **`scripts/ai-search/Deploy-AllIndexes.ps1`** (v3.2 path fix per Fable H-1 — v3.1 said `infrastructure/ai-search/`; that directory contains only the schema JSONs + delete/index/test utility scripts, not the deployer). The 7 JSON schema files in `infrastructure/ai-search/` remain the source-of-truth for schema; the script is the source-of-truth for the catalog (which 7 are canonical) + invariants. Idempotency key `aisearch-{customerId}-{indexVer}` where `{indexVer}` = git SHA of `infrastructure/ai-search/` + `scripts/ai-search/Deploy-AllIndexes.ps1`.

**Action items for Phase A** (per INVENTORY §12 verification backlog + Fable H-2):
1. Verify all 7 canonical JSON schemas exist on disk (`spaarke-{files,discovery,records,rag-references,insights,session-files,invoices}-index.json`) and match `Deploy-AllIndexes.ps1` `$Catalog` variable
2. Confirm `spaarke-knowledge-index-v2.json` in `_archive/` is not referenced by any live code
3. Confirm `spaarke-playbook-embeddings.json` is fully deleted (retired per FR-P2-06); verify no `PlaybookEmbeddingService` code remains
4. Cross-check field-level schemas against current BFF service field usage
5. Standardize any naming inconsistencies (e.g., the dev-only `spaarke-invoices-dev` suffix per §18 item 5)
6. **Complete the `GraphAppRoles.cs` GUID population** (r3 task 062 constant has 11 of 14 `AppRoleId` = null) via `az` enumeration of the Graph resource SP — REQUIRED before first production customer provisioning

---

## 9. Identity & Access Specification

### 9.1 Entra App Registrations (BFF API — v3.5: Model 1 = 1 shared instance total, Model 2 = 1 per customer; heading corrected from stale "2 Per Customer" — the Dataverse S2S app-reg counted in that heading was dropped by r3 task 060)

**v3.5 tenancy note (2026-08-19, per auth-v4 coordination — corrects the doc contradiction the v3 note below created; supersedes it)**: Do **not** create a per-customer Entra tenant. **Model 1 (shared trial/SMB)** uses **one shared multitenant BFF app registration** — sign-in audience `AzureADMultipleOrgs`, matching the live app object — created **once**, never per customer; every Model 1 customer authenticates through this single app-reg, with per-customer trust captured via the H0.5/D18 consent-callback (`tid` capture) rather than a distinct app object per customer. **Model 2 (dedicated stamp)** provisions a **per-customer** BFF app registration for tenant-level isolation: Spaarke-hosted Model 2 deployments register the app in the **Spaarke tenant**; customer-owned Model 2 deployments register it in the **customer's own tenant** post-admin-consent (D18). Each Model 2 app-reg lives in whichever tenant hosts that specific customer's deployment — this is the shape the sentence below ("the app registrations below live in whichever tenant hosts the deployment") correctly describes for Model 2. **What this note does NOT license**: a Spaarke-owned app-reg object with customer-tenant compute (i.e., one Spaarke-owned application object serving a customer's UAMI in the customer's own tenant) — that shape was explicitly ruled out by owner decision 2026-08-18, because FIC credentials attach to the application object, which would stay Spaarke-tenant-resident and could never be trusted by a customer-tenant UAMI. See FR-39 + `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` §5.2. `AzureADMultipleOrgs`, the D18 consent-callback endpoint, and U-CB-3 re-consent remain **correct and necessary — scoped to Model 1's shared app-reg specifically.**

~~**v3 tenancy note (per PROJECT-UPDATE §3, superseded by the v3.5 note above)**: Do not create a per-customer Entra tenant. Use one Spaarke tenant + one multitenant BFF app for Model 1 (shared trial) and, for Model 2 customer-owned tenants, register the same multitenant BFF app in the customer tenant (per D18 consent-capture). The app registrations below live in whichever tenant hosts the deployment; the sign-in audience is `AzureADMultipleOrgs` for Model 2 to enable customer-tenant self-service (v3 change from v2's single-tenant `AzureADMyOrg`).~~

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

**v3.5 note (2026-08-19)**: this table is genericized across both models — Model 1 has exactly ONE live instance (see the v3.5 tenancy note above); Model 2 creates one per customer. The confidential-credential mechanism (client secret vs FIC) is **pluggable per FR-39** during auth-v4's phased rollout, not a fixed "24 months, always a secret" story going forward — see §9.6's cross-reference for how this differs from Path X.

**API Permissions (5):**

| API | Permission | Type | GUID |
|-----|-----------|------|------|
| Microsoft Graph | Files.ReadWrite.All | Delegated | `75359482-378d-4052-8f01-80520e7db3cd` |
| Microsoft Graph | Sites.ReadWrite.All | Delegated | `89fe6a52-be36-487e-b7d8-d061c450a026` |
| Microsoft Graph | User.Read | Delegated | `e1fe6dd8-ba31-4d61-89e7-88639da4683d` |
| Microsoft Graph | Mail.Send | Delegated | `e383f46e-2787-4529-855e-0e479a3ffac0` |
| Dynamics CRM | user_impersonation | Delegated | `78ce3f0f-a1ce-49c2-8cde-64b5c0896db4` |

#### ~~Dataverse S2S App Registration~~ **REMOVED v3.2 (r3 task 060 dropped the vestigial app-reg)**

The vestigial Dataverse S2S app registration and its associated KV secrets (`Dataverse-S2S-ClientId`, `Dataverse-S2S-ClientSecret`) were dropped by **r3 task 060** because they had **zero code consumers** — the BFF's Dataverse access uses the single Dataverse Application User (registered as the BFF app-reg + UAMI SP). Reference: [`../code-quality-and-assurance-r3/notes/r3-handoff.md`](../code-quality-and-assurance-r3/notes/r3-handoff.md) §1.

r1 H3 provisions the BFF API app-reg — **v3.5 split**: ONE per customer for **Model 2**; for **Model 1**, the single shared instance already exists and H3 is a no-op for app-reg creation (see §4.1 H3 row + §9.1 tenancy note above). Do NOT re-introduce the S2S app-reg, either model. The BFF's shared-lib Dataverse camp ClientSecret migration (r3 handoff labels this "#3b") is a separate architecture project owned by task 011 (Idea #742, NG1) — NOT r1's scope.

### 9.2 Managed Identity — v3.2 correction: UAMI migration is aspirational, Phase C work

**Current reality (as of 2026-08-15 net10 baseline)**: BFF App Service uses **System-Assigned Managed Identity** — `infrastructure/bicep/modules/app-service.bicep` sets `identity: enableManagedIdentity ? {...}` (a System-Assigned MI pattern). **No `uami.bicep` module exists yet.** v3.1's claim "UAMI provisioned by `uami.bicep`" was aspirational and got merged as if done — v3.2 corrects the record per Fable H-3.

**Design target (Phase C — this project's remaining work)**:

1. **New Bicep module `infrastructure/bicep/modules/uami.bicep`** — creates a User-Assigned Managed Identity per customer environment
2. **Refactor `app-service.bicep`** — accept a UAMI resource ID as a param, set `identity: { type: 'UserAssigned', userAssignedIdentities: {...} }`, bind to BOTH the production slot AND the staging slot
3. **Migrate all RBAC assignments** (KV Secrets User, Storage Blob Data Contributor, Cognitive Services User, Cosmos DB Data Contributor) from the System-Assigned MI principal ID to the UAMI principal ID
4. **Migrate Graph app-role grants** onto the UAMI service principal (per T3 constant `GraphAppRoles.cs`)
5. **Migrate Dataverse Application User registration** from the System-Assigned MI app ID to the UAMI app ID
6. **`keyVaultReferenceIdentity` PATCH** to the UAMI resource ID (per T1)

**Why UAMI over System-Assigned MI (rationale for Phase C investment)**:
- **T5 structurally solved**: single UAMI binds to both slots; slot-swap no longer changes the identity → no cold-start KV-ref failures. Currently with System-Assigned MI, each slot has a distinct MI principal, requiring RBAC to be granted twice (per T5 interim mitigation).
- **Cross-resource RBAC before App Service exists**: UAMI can be created first + RBAC-assigned to KV/Cosmos/Storage before the App Service is deployed. With System-Assigned MI, the identity doesn't exist until the App Service is created, forcing a two-phase deploy (create app service → grant RBAC → restart to pick up).
- **Slot-swap parity**: Production and staging slots consume the same identity → identical behavior; no per-slot config divergence.
- **Migration to task 011 (#3b)**: the eventual shared-lib `ClientSecret`→MI migration for the Dataverse camp requires a stable MI identity that outlives any single App Service — UAMI is the pre-requisite.

**Interim state (until Phase C lands)**: H4 grants KV RBAC to BOTH slots' distinct System-Assigned MI principals; T5 verification query enumerates both. This works but is fragile — Phase C is scheduled for early implementation to close T5 structurally.

**Do not conflate the two UAMIs**: this section's UAMI is the *customer-stamp* identity (per-customer, consumed by the customer's BFF). The *L2 control-plane* UAMI and its admin-env Dataverse App User are §9.6.

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

### 9.6 L2 Control-Plane Identity — Path X (added v3.4 per DS-8)

**Decision (locked 2026-08-18)**: all L2 reads/writes to the ADMIN Dataverse environment — registry lookups (H0.5 re-consent, `environmentId` resolution), the `sprk_currentrunid` I5 guard, and H13's `sprk_setupstatus = Ready` PATCH — authenticate as the **L2 UAMI registered as a Dataverse Application User** on the admin env, holding the scoped custom security role **`Spaarke Provisioning Registry`** (org-level Read/Write/Create/Append on `sprk_dataverseenvironment` + minimum basics — deliberately NOT System Administrator), tokens via `DefaultAzureCredential(ManagedIdentityClientId)` with scope `{adminEnvUrl}/.default` — the same idiom H10's `DataverseWebApiAppUserCreator` and the H5 health probe already use in-process.

**Why**: the only ADR-028-compliant option ("MUST use `DefaultAzureCredential` for all server outbound — NOT `ClientSecretCredential`"); first-party supported (PPAC accepts MI Application IDs for app users, Microsoft Learn ms.date 2026-04-03; `pac admin assign-user --application-user`); the repo already ships the exact registration code (H10) and the L2 code headers pre-declare this migration (`CustomerRunGuardOptions.cs` "FUTURE MIGRATION" block); gives L2 a **distinct, auditable Dataverse identity** with its own service-protection budget instead of impersonating the BFF's systemuser as SysAdmin; **zero rotation surface** (platform-managed credential). Path Y (BFF app-reg client secret) rejected: new documented ADR-028 violation, false audit attribution, permanent rotation runbook, widened blast radius of a BFF secret leak.

**Mechanics**: one-time per-env idempotent `Grant-ControlPlaneIdentity.ps1` — role-ensure → app-user-ensure (find-by-`applicationid` → POST `/systemusers` → `systemuserroles_association/$ref`) → `WhoAmI` verify; the same script carries the L2 UAMI's Graph app-role grants (C5.8) — one identity script for the control plane. Data-plane operation, not ARM — no Bicep. No admin consent exists or is needed (Dataverse authorizes via security roles; creating the systemuser row IS the authorization act).

**Deletions this decision drives**: L2 stamp Bicep `dataverseClientSecretName` param + KV-ref emission; `CustomerRunGuardOptions.ClientId/ClientSecret` fields + `Validate()` clauses; the dummy-secret bug #18 dies at source. **What does NOT get deleted**: ~~the `Dataverse-ClientSecret` KV **secret** (the BFF shared-lib path consumes it until NG1 #3b — BINDING never-delete). H4's *customer-side* `Dataverse-ClientSecret` seeding also stays (the customer BFF is still secret-based until #3b — explicitly not r1's migration).~~ **Superseded 2026-08-25 per §6.5 resolution + auth-v4 A4/E-3 landings**: `Dataverse-ClientSecret` KV secret retained through 2026-11-23 as auth-v4's rollback copy (obligation 051-E owns retirement); customer BFF migrated to secret-free via A38a's H4 omit contract (H4 no longer seeds `BFF-API-ClientSecret` on secret-free envs). See spec.md MUST + `.claude/constraints/provisioning.md` §KV credential lifecycle.

**Failure modes**: UAMI disabled → loud `CredentialUnavailableException` with a ≤24 h cached-token tail (accepted; writes fail closed as `InfraFault`/Resumable); systemuser or role removed → loud 401/403, restored in seconds by re-running the grant script; H13's live-probe set includes "L2 systemuser exists + role assigned". **Cross-tenant**: MI tokens are home-tenant-only — an *enforcement* of registry-writes-are-admin-env-only, not a limitation. The sanctioned future cross-tenant path (customer-owned-tenant Model 2 writes; secretless NG1 #3b) is **MI-as-FIC on a multitenant app-reg (Path Z, GA)** — noted for r2+, not built in r1.

**v3.5 clarification (2026-08-19, per auth-v4 coordination) — do not conflate Path X with auth-v4's BFF-OBO credential migration.** Path X (this section) governs the **L2 control-plane's own** Dataverse credential for registry reads/writes against the ADMIN environment — an L2-internal identity concern, entirely unaffected by auth-v4's change request; nothing in FR-39 changes Path X. Auth-v4's FIC migration (FR-39, ADR-028 Amendment A4 + Exception E-3) is a **separate concern**: it governs the **customer BFF's** outbound OBO confidential-client credential (the `BFF-API-ClientSecret` → FIC transition for delegated Graph/Dataverse/Power BI/M365-Copilot calls), owned by H3/H4 per the split at §4.1's H3 row and §9.1's tenancy note. Path Z (MI-as-FIC for L2's own future cross-tenant registry writes, noted above) is likewise distinct from auth-v4's migration — Path Z, auth-v4's BFF-OBO FIC, and r1's Model 2 H3 FIC all use the same underlying **MI-as-issuer** mechanism (this is exactly the mechanism auth-v4's §4 cap analysis and R23's closure, §12, are about), but they serve three different credential stories: L2's own registry writes (Path X today, Path Z if ever needed), and the customer BFF's OBO exchange (FR-39, now — not "if ever needed"). Three stories, one shared mechanism — keep them separate when reading either project's docs.

### 9A. Consolidated Identity + Configuration Surface (per-customer) — added v3.3

**Purpose**: single-page reference for "what identity + config surface does one customer environment carry?" Distilled from §7.7 (KV secrets), §7.9 (naming), §9.1 (app-reg), §9.2 (MI), §9.3 (Dataverse security), §9.4 (Exchange policies), §9.5 (webhooks), §10.2 (parameters), §10.3 (env vars), §10.4 (BFF app settings). When those sections and this table disagree, **this table is the reconciled current-state view**; the individual sections carry the depth.

| # | Artifact | Where it lives | Who provisions | Who verifies | Rotation cadence | Model 1 vs Model 2 |
|---|---|---|---|---|---|---|
| 1 | **BFF API app registration** — **v3.5 split (2026-08-19)**: Model 1 = ONE shared instance (not per-customer); Model 2 = 1 per customer | Model 1: Spaarke tenant, single shared object · Model 2: Spaarke tenant (Spaarke-hosted) or customer tenant (customer-owned, post-consent D18) | H3 (Model 1: no-op — verifies shared instance; Model 2: creates per-customer instance + FIC per FR-39) | H13 acceptance | Client secret retained as ordered fallback through auth-v4 Phase 5, then FIC (FR-39); secret **NEVER remove** while it exists (per r3 handoff) | Model 1: 1 shared app-reg for ALL customers · Model 2: 1 app-reg per customer, in whichever tenant hosts that deployment |
| 2 | ~~Dataverse S2S app-reg~~ | ~~—~~ | — | — | — | **DROPPED v3.2 (r3 task 060)** — zero code consumers |
| 3 | **User-Assigned Managed Identity** (`mi-spaarke-{customerId}-{env}`) | Customer subscription (Model 2) or Spaarke platform sub (Model 1 shared floors) | H2a (Phase C — new `uami.bicep`) | Trap T5 verification | N/A (identity, not credential) | Per-customer in both models |
| 4 | **Dataverse Application User** (2 registrations: BFF app-reg + UAMI) | Customer's Dataverse environment | H10 | Trap T2 verification (Dataverse `systemusers` query) | Never expires; **NEVER remove per r3 handoff** | Both models — Dataverse env is dedicated per §3A A1 |
| 5 | **Key Vault** | Customer subscription | H2a (Bicep with vault name as param per §7.9) | ARM read | N/A | Model 1: `sprk-{env}-kv` shared · Model 2: `sprk-{customerId}-{env}-kv` dedicated · **Dev exception**: `spaarke-spekvcert` |
| 6 | **KV secrets** (10 canonical Infrastructure + 4 Auth + 4 Integration = ~18 secrets) | KV per #5 | H4 seeder (from Phase H canonical secret-catalog manifest) | Trap T1 verification (`keyVaultReferenceIdentity` patched) + boot-time fail-fast (r3 task 061 `ValidateOnStart`) | Client secrets 24-month; webhook signing keys ad hoc (regenerate → rotate consumers) | Same in both models |
| 7 | **Dataverse env variables** (7 per-customer values) | Customer Dataverse env (`environmentvariablevalue` records) | H7 | Client startup fail-fast (no hardcoded URL fallbacks; per task 024) + H13 sample-query check | Rotate with resource replacement (rare) | Same in both models |
| 8 | **BFF `IOptions<T>` classes** (26 sections: 24 Tier-1 validated-on-start + 2 Tier-2 kill-switch-gated + Tier-3 defaults) | BFF App Service settings + KV references (form: `@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=...)`) | H4 + H9 (BFF deploy) | r3 task 061 `.ValidateDataAnnotations().ValidateOnStart()` at boot — `/health` probe fails if any Tier-1 missing/invalid | KV secret rotations propagate automatically | Same in both models |
| 9 | **Graph app-role grants** (14 per `Infrastructure/Auth/GraphAppRoles.cs`, r3 task 062 constant) | Granted on **UAMI service principal** by H10 (post-step) | H10 + T3 verification (Graph query: UAMI SP `appRoleAssignments` includes all 14) | Nightly parity ArchTest (queued behind CI-wiring per r3 `task-042-063-ci-gate-wiring-deferral.md`) | Admin-consent-per-tenant is a one-time customer action; grants themselves don't expire | Same in both models |
| 10 | **Exchange ApplicationAccessPolicies** (2 total: BFF app-reg + UAMI, both mandatory per T4) | Customer Exchange Online tenant (Model 2) or Spaarke Exchange tenant (Model 1) | H14 sub-step (a) — create-if-missing then verify | Trap T4 verification (`Get-ApplicationAccessPolicy` returns 2 entries; both principals) | 30-min propagation post-create; policies don't expire; **future migration to Exchange RBAC for Applications tracked in R22 v3.3** | Same in both models |
| 11 | **Webhook signing keys** (2: `communication-webhook-signing-key` + `Email-WebhookSigningKey`) | KV per #5 | H4 (generated 48-byte base64) | H14 sub-step (b/c) — subscription create then signature verification round-trip | Regenerate + re-subscribe (rare — coordinated maintenance window) | Same in both models |
| 12 | **SPE container-type + root container ID** | KV secret `customer-{customerId}-spe-container-id` + Dataverse env-var `sprk_SharePointEmbeddedContainerId` | H8 (confidential-client fix per T6) | Trap T6 verification (container GET via app-only token) | Never rotate; container = data | Per-customer isolated by both KV boundary + Dataverse env boundary; **Invariant 4 per §4D** |
| 13 | **Customer `tid` (Entra tenant ID)** | Dataverse env-var `sprk_TenantId` + Cosmos ProvisioningRun.parameters.tenantId | H0.5 (consent-callback for Model 2) or H0 param (Model 1) | H13 sample query verifies BFF sees the right tenant | Never rotates for a given customer | Model 1: Spaarke tid · Model 2: customer tid |
| 14 | **Per-tenant token budget** (`tokenBudgetMonthlyUSD`, D19) | Cosmos ProvisioningRun.parameters + APIM policy state | H0 param + D19 metering layer | H13 verifies budget enforcement (attempt over-budget → blocked) | Ops-driven (upgrade/downgrade) | Model 1: capped (trial); Model 2: unlimited |
| 15 | **L2 control-plane UAMI + admin-env Dataverse App User** (`Spaarke Provisioning Registry` scoped role) | Spaarke platform sub (UAMI, Bicep-owned in L2 stamp) + admin Dataverse env (systemuser row) | `platform-controlplane.bicep` (UAMI) + one-time `Grant-ControlPlaneIdentity.ps1` (App User + role + Graph app-roles) | H13 control-plane self-probe (`systemusers` query + role check) + canary registry-write attribution | **None — platform-managed; no expiry cliff (the point of Path X)** | Identical in both models (registry lives only in the admin env) |

**Rotation summary** (things that expire and must be rotated to keep the customer operational):
- **BFF-API-ClientSecret**: 24-month expiry → alarm at expiry-30-days → rotate + push to KV + BFF picks up via KV reference (zero downtime if done right)
- **Webhook signing keys**: no expiry, but rotated on incident (leak, algorithm upgrade) — coordinated with re-subscription of every Graph/service-endpoint webhook
- **Everything else**: identity artifacts (UAMI, MI SP, Dataverse App User) don't expire; RBAC / role grants don't expire

**One-page mental model**: Model 2 customer environment = **1 per-customer BFF app-reg (+ FIC per FR-39) + 1 UAMI + 1 Dataverse env with 2 App Users + 1 KV with ~18 secrets + 1 SPE container-type/root container + 14 Graph app-roles on UAMI + 2 Exchange policies + 7 Dataverse env-var values + 2 webhook signing keys + 1 customer tid**. Model 1 differs in: **the shared multitenant BFF app-reg (one instance for ALL Model 1 customers, not per-customer — v3.5 split)**, KV shared as `sprk-{env}-kv`, 3 fixed-floor Azure resources (App Service Plan, OpenAI, AI Search) shared with metering.

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
| `Provision-Customer.ps1` | `scripts/` | **PORT + MAJOR EXTEND** | 13 steps → handler catalog. State-file resume → ProvisioningRun record. **v3.2 (Fable M-2 honesty)**: current step 3 only provisions Storage+KV+ServiceBus. H2a expansion adds SIX new module invocations: **Cosmos DB** (R11), **OpenAI**, **AI Search**, **Document Intelligence**, **App Insights + Log Analytics**, **optional SignalR** — NOT just Cosmos. Header comment already states "per-customer Redis is DEPRECATED (Q-E FR-12)" — v3.2 removes Redis from H2a scope. |
| `Deploy-RedisCache.ps1` *(v3.2 added)* | `scripts/` | **REUSE (per-env)** | Deploys Redis per environment per Q-E FR-12 (NOT per-customer). Consumed once per env during platform bootstrap; H2a does NOT invoke. |
| `Build-SpaarkeMaster.ps1` | `scripts/` | **REUSE (authoritative)** | Machine composition of 386-component solution. INVENTORY §0 source of truth. |
| `Deploy-DataverseSolutions.ps1` | `scripts/` | **REUSE + EXTEND** | Called by H6. **v3**: extend to Package Deployer invocation for dependency-ordered import per INVENTORY §1 (~10 managed solutions). |
| `Deploy-BffApi.ps1` | `scripts/` | **REUSE** | Called by H9. |
| `Deploy-Release.ps1` | `scripts/` | **REUSE + HARDEN (Gap 2)** | Called by H9. **v3**: Phase 4 must be `customerId`-driven; remove `spaarkedev1` hardcode. |
| `Validate-DeployedEnvironment.ps1` | `scripts/` | **REUSE + EXTEND (Gap 4)** | Called by H13. **v3.2**: extend to end-to-end acceptance gate — sample analysis, sample document upload+index, workspace-layout render, wizard field-map, **all 6 §4B silent-fail traps cleared**, `naming-conformance-check.ps1` exits 0. |
| `Test-Deployment.ps1` | `scripts/` | **REUSE** | Smoke-test handler. |
| `Register-EntraAppRegistrations.ps1` | `scripts/` | **PORT (Gap 3)** | Basis for H3. **v3.2**: needs full idempotency for **~14 permission grants** (v3.1 said ~11; corrected against `GraphAppRoles.cs` r3 task 062 constant); **only ONE app-reg** (BFF API) — r3 task 060 dropped the Dataverse S2S app-reg; admin consent handled via H0.5 consent-callback for Model 2. |
| `Create-NewContainerType.ps1` + `Register-*.ps1` + `New-BusinessUnitContainer.ps1` | `scripts/` | **PORT + FIX (T6)** | Basis for H8. **v3**: switch to **confidential-client (app-only) token** — delegated token 403s (`public client not allowed`) per INVENTORY §10. Cert bootstrapped from KV via `Import-And-Register.ps1`. |
| `Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1` + `Deploy-*` (seed layer) | `scripts/seed-data/` + `infra/dataverse/**` | **PORT (Gap 1)** | Basis for H12a/b/c. **v3**: resolve two-source drift (`scripts/seed-data` MVP vs `infra/dataverse` R7) via declarative seed manifest. |
| `Deploy-AllIndexes.ps1` | **`scripts/ai-search/`** *(v3.2 path corrected — v3.1 said `infrastructure/ai-search/` which only has the JSON schemas)* | **REUSE** | Invoked by H2b — 7 canonical indexes per FR-07. `spaarke-playbook-embeddings` retired; `spaarke-knowledge-index` archived. |
| `naming-conformance-check.ps1` *(v3.2 added, r3 task 063)* | `scripts/` | **CONSUME (advisory-until-remediated)** | H13 acceptance gate invokes; must exit 0 on r1-owned surfaces post-Phase G naming remediation. |
| `Decommission-Customer.ps1` | `scripts/` | **OUT OF SCOPE** (D17) | Remains operational as-is. Registry-aware teardown deferred to r2. |
| `/deploy-new-release` | `.claude/skills/` | **REUSE as-is** | Out of scope. Reference model for L3 skill UX. |

### 11.1a Solutions Reconciliation — what actually ships vs what's in the repo (added v3.3 per Q2)

Design v3.1/v3.2 said "~10 managed solutions"; INVENTORY §1 says the same. **The actual authoritative count is 8** per `scripts/Deploy-DataverseSolutions.ps1` `$SolutionImportOrder`. This section resolves the confusion once.

**Three different "solution" concepts in the repo — v3.3 disambiguates**:

| Source | Count | What it is | Ship to customers? |
|---|---|---|---|
| `src/solutions/` folders | **36** | Mix of managed solutions, code-page SPAs (deployed as web resources, not solutions), wizards, and dev-only tools | **Not all** — see reconciliation below |
| `src/dataverse/solutions/` folders (`spaarke_core`, `spaarke_containers`, `spaarke_documents`) | 3 | Unpacked solution skeletons per INVENTORY §1 ALM note | **No** — these are dev-time unpacked forms, not the source of truth for shipping |
| `Deploy-DataverseSolutions.ps1` `$SolutionImportOrder` | **8** | Authoritative list the deployer imports (with dependency order + tier) | **Yes — these 8 are what H6 ships** |

**Authoritative 8 managed solutions shipped by H6** (per `Deploy-DataverseSolutions.ps1:125-135`):

| Tier | Solution folder | Solution unique name | Dependency |
|---|---|---|---|
| 1 | `SpaarkeCore` | `SpaarkeCore` | Base — entities, option sets, security roles, MDA shell |
| 2 | `webresources` | `SpaarkeWebResources` | JS files referenced by forms + ribbons; depends on Tier 1 |
| 3 | `CalendarSidePane` | `CalendarSidePane` | Tier 3 — independent |
| 3 | `DocumentUploadWizard` | `DocumentUploadWizard` | Tier 3 — independent |
| 3 | `EventCommands` | `EventRibbons` | Tier 3 — event ribbon JS |
| 3 | `EventDetailSidePane` | `EventDetailSidePane` | Tier 3 — independent |
| 3 | `EventsPage` | `EventsPage` | Tier 3 — independent |
| 3 | `LegalWorkspace` | `LegalWorkspace` | Tier 3 — independent |

**The other ~28 items in `src/solutions/` — what happens to them?**

Most are **code-page SPAs deployed as web resources**, NOT as managed solutions. The deployment mechanism differs:

- **Code pages** (SpaarkeAi, EmailPage, PlaybookLibrary, DailyBriefing, Notepad, AllDocuments, FindSimilarCodePage, SpeAdminApp, Reporting, SmartTodo, WorkspaceLayoutWizard, EventsPage, EventDetailSidePane, CalendarSidePane, DemoRegistration, EmailPage, ...): built via `npm run build` in each folder → produces `dist/*.js` bundles → deployed as **web resources** via `Deploy-Release.ps1 Phase 4` (customer-scoped per Gap 2 hardening). These become web resources INSIDE the `webresources` solution (Tier 2) OR feature solutions.
- **Wizards** (7 CreateXxxWizard folders): actually feature-solution-scoped — built and packaged inside the appropriate feature solution
- **Non-SPA content** (CopilotAgent = M365 declarative agent manifest, spaarke_insights = solution staging, sprk_communicationconversationpage = internal): deployed via their own specialized tooling
- **Retired / dev-only**: some folders may not deploy anywhere (verified in Phase A)

**v3.3 obligation on Phase A**: audit each of the ~28 non-deployer-listed items in `src/solutions/` and mark each as (a) code-page deployed via `Deploy-Release.ps1`, (b) feature-solution-scoped inside one of the 8 shipped solutions, (c) dev-only / retired, or (d) unknown-needs-review. Publish results at `notes/solutions-reconciliation-2026-08.md`.

**Design implication**: r1's H6 handler ships **8 managed solutions**, NOT 10. Every place in the design that says "~10 managed solutions" — §1 Executive Summary, §11.1 disposition table, §11 header, PROJECT-UPDATE §2 — must be reconciled to 8 (or updated to a corrected authoritative count if Phase A reveals additions). **v3.3 correction on the immediate references only**; PROJECT-UPDATE is a companion doc that will need its own update.

**INVENTORY §1's "10 solutions (386 components)"**: the 386-component count is authoritative (from `Build-SpaarkeMaster.ps1`); the "10 solutions" count is the drift. Reconcile INVENTORY as a Phase A action.

### 11.2 Infrastructure-as-Code (v3.2 updated)

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `customer.bicep` | `infrastructure/bicep/` | **REUSE + EXTEND** | Extend for dedicated OpenAI/Search/DocIntel/AppInsights per D3/D12 **+ Cosmos DB (BFF prereq) + optional SignalR**. **v3.2**: remove Redis (Q-E FR-12); vault-name parameter (Phase G); UAMI param post-Phase C. |
| `platform.bicep` | `infrastructure/bicep/` | **REBUILD** | Shrinks to control-plane-only: L2 App Service (B2), Cosmos DB (control-plane `spaarke-provisioning`), platform KV (parameter secrets), monitoring (D12). |
| **25 Bicep modules** *(v3.2 count corrected — v3 said 26; 1 was a `.json` lifecycle policy)* | `infrastructure/bicep/modules/` | **REUSE + ADD `uami.bicep` (Phase C)** | Composable building blocks. **v3.2 additions**: new `uami.bicep` module (Phase C) + refactor `app-service.bicep` to consume UAMI + fix `cosmos-db.bicep` name (v3.1 said `cosmos.bicep`). |
| `model1-shared.bicep` + `model1-customer.bicep` + `model2-full.bicep` | `infrastructure/bicep/stacks/` | **REUSE (all three first-class per §3A A1)** | `model2-full` = D3 default dedicated; `model1-shared` = trial tier. |
| **NEW: L2 control-plane Bicep** | `infrastructure/bicep/platform-controlplane.bicep` *(new)* | **NEW** | App Service (B2) + Cosmos DB + platform KV for the L2 orchestrator. |
| **DEFERRED: Terraform Power Platform provider** *(v3.2 deferred per M-10)* | `infrastructure/terraform/dataverse/` *(future)* | **DEFERRED to first-customer engagement** | v3 D14 hybrid tooling per §4A remains the design intent. Interim: H5 uses `pac admin` PS invocation; H10 uses PPAC UI + Graph SDK. TF migration lands as its own task chain once customer volume justifies the ops cost. |
| **NEW: `sprk-provisioning-jobs` queue (IaC-declared)** | `platform-controlplane.bicep` (child resource on the existing SB namespace via `existing` reference — NOT `modules/service-bus.bicep`, whose uniform properties are the wrong shape) | **NEW (v3.4, C5.4/C4.6)** | `requiresSession: true` + `requiresDuplicateDetection: true` (`PT1H`) + `lockDuration PT5M` + `maxDeliveryCount 10` + DLQ-on-expiry. Both properties create-time-only → live queue delete + Bicep recreate (drain-verify; RBAC survives). SB Data Sender + Receiver role assignments for the L2 UAMI land in Bicep alongside (C5.5, membership-topic.bicep pattern). |
| **NEW: EXO sidecar image + sitecontainer** | ACR repo + `platform-controlplane.bicep` sitecontainer config + CI workflow stage | **NEW (v3.4, §4.2a)** | pwsh 7.4 + pinned ExchangeOnlineManagement + one script + HTTP listener; ≤250 MB ceiling, Trivy-gated; monthly rebuild cadence. |

### 11.3 BFF Job Handler Ecosystem

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `IJobHandler` + 13 production handlers + `ServiceBusJobProcessor` | BFF `Services/Jobs/`, `Services/Ai/Jobs/` | **REFERENCE ONLY (v3.4)** | Pattern exemplars for handler shape, idempotency, telemetry, and the dispatcher's BackgroundService+processor shape. **Never a compile-time or runtime dependency**: L2 defines `IProvisioningHandler` + its own `ProvisioningHandlerDispatcher`; the BFF processor drains a different queue and registers no provisioning handlers. |
| `JobSubmissionService` | BFF `Services/Jobs/` | **RESOLVED: not used (v3.4 — closes the v3 "ASSESS")** | L2 has its own `ServiceBusHandlerEnqueuer` (deterministic MessageId incl. `attempt`, `SessionId=CustomerId`). Envelope mirrors the BFF's Subject/ApplicationProperties shape for observability parity only. |
| `IdempotencyService` (Redis) | BFF `Services/Jobs/` | **PATTERN REUSE** | L2's `DispatchIdempotencyService` mirrors it at the dispatcher dequeue path (L2 of the 3-level scheme). |

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

## 12. Risk Register (v3.2 refreshed 2026-08-15)

Absorbed from the 13 known deployment-guide issues + r1 carry-overs + 2026-08-12 assessment findings + r3 handoff (2026-08-14) + Fable adversarial review (2026-08-15). v3 added R10–R16; v3.2 adds R17–R21 + updates R2/R15/R4 for r3-shipped changes; v3.3 adds R22–R23; **v3.5 (2026-08-19) closes R23** per auth-v4's corrected MI-as-issuer cap analysis.

| ID | Risk / known issue | Source | Design must... |
|---|---|---|---|
| R1 | SPE container-type creation — `westus` billing requirement + up-to-24h replication delay. | ENV-GUIDE + INVENTORY §10 | **v3**: replication delay is **lead-time** (§9 north star), not in-pipeline wait. H8 initiates; lead-time item on customer prereq checklist. |
| R2 | Dataverse application user creation — v2 said PPAC-UI-only. | v2 finding | **v3 design target**: TF Power Platform provider `powerplatform_user` resource — fully automated (D14 v3). **v3.2 (M-10) interim**: PPAC UI + Graph SDK for role sync; TF migration deferred to first-customer engagement per dev-only reality. |
| R3 | Solution export/fix pipeline is 8 manual sed-style steps; managed-vs-unmanaged changes it (D1). | ENV-GUIDE §6 | H6 scripts export→fix→pack-managed→verify + Package Deployer; no manual edits. |
| R4 | Entra app reg — **14 permission GUIDs** per `GraphAppRoles.cs` (v3.2 corrected from v3's "~11") granted by hand; no recovery script. | ENV-GUIDE §4 + INVENTORY §7 T3 + `Infrastructure/Auth/GraphAppRoles.cs` | H3 scripts grants idempotently against the code constant; admin-consent is a verified gate for Model 2 (D18 consent-callback). **v3.2 additional obligation**: complete the 11 of 14 null `AppRoleId` GUIDs in `GraphAppRoles.cs` via `az` enumeration BEFORE first production customer. |
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
| **R15 (v3)** | **TF Power Platform provider maturity**: SPs can't create `Developer`-type envs (Sandbox/Production only); SP must be admin-bootstrapped via BAP API once per tenant. | §4A + PROJECT-UPDATE §8 | **v3.2 deferred**: TF adoption pushed to first-customer engagement per M-10; interim H5/H10 use PPAC + PS. Preflight H0 will assert SP BAP-bootstrapped when TF migrates. |
| **R16 (v3)** | **Hardcoded `spaarkedev1` in `Deploy-Release.ps1` Phase 4** — code-page deploy targets dev env regardless of `customerId`. | PROJECT-UPDATE §6 Gap 2 | H9 uses hardened Phase 4 (`customerId`-driven). |
| **R17 (v3.2)** | **KV-secret + resource naming drift** — 4 vault-naming conventions, 3 AI-Search key aliases in 3 casings, env-token baked into replicated names, 6 orphan template references, 2 KV-reference syntaxes. r3 gate (`scripts/naming-conformance-check.ps1`) runs advisory-until-remediated. | r3 task 063 handoff §4a + r3 KV federation design §2.6 D6-01/D6-02/D6-04/D6-08 | **v3.2 Phase G** (naming remediation at provisioning) + **Phase H** (canonical secret-catalog manifest per Phase 3b) — apply canonical names in H4 seed + `sprk-{env}-kv` vault param in Bicep; codify `spaarke-spekvcert` DO-NOT-RENAME dev exception. H13 acceptance runs `naming-conformance-check.ps1` — exit 0 required for `Setup Status = Ready`. |
| **R18 (v3.2)** | **Dev-only baseline** — demo/prod environments decommissioned for budget per r3 CLAUDE.md 2026-08-14. E2E acceptance can no longer regress-test against demo/prod. Trial-environment strategy required. | r3 CLAUDE.md `Only spaarke-dev is live` note | Phase F stands up a fresh `trial-{yyyymmdd}` customer stamp (Model 1 profile) as the E2E acceptance target. No dependency on demo/prod. Cleanup after acceptance is discretionary. |
| **R19 (v3.2)** | **Cross-customer concurrency limits at resource layer** — Azure OpenAI regional TPM quota (150/200/30/350 per model per region) is a **regional** quota; Dataverse env-creation rate limits (~4/hour per tenant typical); Graph API throttling; subscription vCPU/SKU quotas. Two concurrent Model 2 provisions in the same region can fail on quota clash. | Fable §6 item 2 | H0 preflight (v3.2 extended) queries: `az cognitiveservices` for OpenAI TPM headroom; `pac admin quota` for Dataverse env quota; `az vm list-usage` for subscription vCPU. **Fails run before H1 starts** if any headroom insufficient for the +1 provisioning target. |
| **R20 (v3.2)** | **Handler execution model under App Service HTTP timeout** — App Service 230s default request timeout is fatal for 30-min handlers (H2a Bicep, H5 Dataverse env, H6 solution import). | Fable M-9 | **§4.2 (v3.2) handler execution model**: L2 REST endpoints ENQUEUE handlers via Service Bus + return 202 Accepted; state-reconciler `BackgroundService` in L2 polls Cosmos every 5s to advance the DAG. Handlers run in **L2's own `ProvisioningHandlerDispatcher`** (ADR-004-shaped `IProvisioningHandler` contract; v3.2 originally said "existing BFF `IJobHandler` infrastructure" — corrected v3.4, see §4.2). No handler runs synchronously in the HTTP request path. |
| **R21 (v3.2)** | **UAMI migration debt** — v3.1 asserted UAMI provisioned by `uami.bicep`; actual is System-Assigned MI (no `uami.bicep` module). T5 slot-swap failure mode intrinsic to System-Assigned MI persists until UAMI Phase C lands. | Fable H-3 + `infrastructure/bicep/modules/app-service.bicep` inspection | **Phase C** (new): create `uami.bicep` module + refactor `app-service.bicep` to consume UAMI + bind both slots → T5 structurally impossible. Interim H4 grants KV RBAC to both slots' System-Assigned MI principals separately. |
| **R22 (v3.3)** | **Exchange ApplicationAccessPolicy → RBAC for Applications migration (medium-term)** — Microsoft's Aug 2026 status: RBAC for Applications is now the recommended pattern; the legacy `Set-ApplicationAccessPolicy` doc is titled `(legacy)`. **No hard cutover date**; coexistence is safe (additive). r1 continues using `Set-ApplicationAccessPolicy` for now (T4). | Q5 research spike ([`notes/graph-spe-2026-08-standards-spike.md`](notes/graph-spe-2026-08-standards-spike.md)) | Add Phase D backlog item "migrate Exchange app-access to RBAC for Applications" for consideration in r2 or when Microsoft announces sunset date. r1 acceptance criteria unchanged for now — H14 continues creating both policies per T4. |
| **R23 (v3.3) — CLOSED v3.5 (2026-08-19)** | **MI-as-Federated-Identity-Credential opportunity for Model 2 secretless** — GA'd 2026; enables cross-tenant Graph app-only without secrets (20-FIC-per-app cap). Not needed for r1's current design (which uses per-tenant BFF app-reg with client secret per §9.1) but material Phase C+ optimization for Model 2 cross-tenant scenarios. | Q5 research spike | ~~Phase D backlog item; not r1 acceptance. If adopted, replaces the `BFF-API-ClientSecret` rotation ceremony entirely for the cross-tenant Model 2 path.~~ **[RESOLVED 2026-08-19 per auth-v4 §4]**: cap does not bind for either project's use case. The MI-as-issuer pattern (used by both auth-v4's BFF-OBO migration and r1's Path Z) places FICs on the **trusting app-registration**, not on the UAMI — so the question is "how many UAMIs must one app-reg trust," and in every deployed shape the answer is one. Reading 1 (Model 1, shared app-reg): 2/20 used on the shared BFF app-reg. Reading 2 (Model 2, per-customer app-reg): 1/20 per per-customer app-reg. r1's original Q5 spike (`notes/graph-spe-2026-08-standards-spike.md` line ~59) conflated MI-as-issuer with MI-as-recipient patterns, which is why it read the cap as a live Phase-C+ concern instead of a non-factor. **No longer a Phase D backlog item — pulled into r1 scope now via FR-39** (Model 2 H3 branch creates the per-customer app-reg + FIC; see §4.1 H3 row + §9.1). |

---

## 13. Scope

### In Scope (v3 — "fully deploy a customer" scope)

**L1/L2/L3 architecture** (per v2, unchanged shape; handler count adjusted):
1. **L1 handler catalog** — **19 handlers** (v3 count: H0, **H0.5**, H1, **H2a/H2b**, H3, H4, H5, H6, H7, H8, H9, H10, H11, **H12a/H12b/H12c**, H13, H14) implementing the provisioning pipeline as idempotent `IProvisioningHandler` implementations
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

**New v3.2 scope (per r3 handoff + owner directive #3 + Fable review)**:
22. **Phase G — Canonical naming compliance at provisioning** (per r3 task 063 handoff §4a): apply canonical KV secret names + `sprk-{env}-kv` vault + `spaarke-spekvcert` DO-NOT-RENAME dev exception in H4 + Bicep param-ization. Owner directive #3: bake into new-customer provisioning; skip live-dev remediation.
23. **Phase H — #1 KV federation remediation** (per r3 task 017 handoff §4b): canonical secret-catalog manifest (Phase 3b of r3 KV federation design) generates seeder + Configure script + tokens doc + Bicep KV secret set. External-spa + code-pages runtime `/config.json` fetch to close bake-at-build-time pattern. Full scope per owner directive #3 "not deferred; done in the context of THIS project."
24. **Phase C — UAMI migration** (v3.2 A3 correction of v3.1 aspirational claim): new `uami.bicep` module + `app-service.bicep` refactor + RBAC migration + Graph app-role migration + Dataverse App User re-registration on UAMI app ID. Structural fix for T5.
25. **§4C rollback semantics** (Fable §6-1): failure classification (Resumable / Retryable-with-cleanup / Quarantine-required / Successful-but-drifted); Cosmos state machine additions (`Quarantined`); new `POST /api/runs/{id}/clear-quarantine` endpoint.
26. **§4.2 handler execution model** (Fable M-9): fire-and-forget via Service Bus + state-reconciler `BackgroundService` in L2 App Service — spelled out; not just implied.
27. **H0 preflight quota checks** (Fable §6-2): OpenAI regional TPM headroom, Dataverse env-creation rate, subscription vCPU. Blocks the run before H1 starts.
28. **§4.1a Model 1 vs Model 2 handler behavior differences** (Fable §6-7): enumerated table — H0/H2a/H2b/H4/H7/H10/H12c/H13 differ per tier; other handlers behave identically.
29. **`GraphAppRoles.cs` completion** (Fable H-3): complete 11 of 14 null `AppRoleId` GUIDs via `az` enumeration; escalation gate before first production customer.

**Acceptance:**
30. **E2E dry run** — stand up a fresh **`trial-{yyyymmdd}` customer stamp** (Model 1 profile per H-6 decision) using only the new pipeline; reach `Setup Status = Ready`; validate all silent-fail traps cleared; naming-conformance-check exits 0.

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

## 14. Phasing (v3.2 refreshed 2026-08-15)

| Phase | Content | Depends on | Notes |
|---|---|---|---|
| **A** | Doc consolidation (Gap 4) + AI Search index schema audit vs `Deploy-AllIndexes.ps1` catalog (7 canonical incl. `spaarke-discovery-index`, EXCL. retired `spaarke-playbook-embeddings`/`spaarke-knowledge-index`) + INVENTORY §12 verification backlog (33-vs-7 PCF, 87-entity roster, two-source AI seed drift, managed-solution export coverage) + doc-drift fixes (R6) + **`GraphAppRoles.cs` GUID completion** (11 of 14 null per Fable H-3 — via `az` enumeration of Graph resource SP) | — | Parallel with B / G |
| **B** | Gap automation scripts — hardened & idempotent (Entra apps 14-grant H3 per R4 v3.2, SPE H8 confidential-client per T6, solution export/fix managed H6, `Deploy-Release.ps1` Phase 4 hardening per Gap 2, Cosmos DB provisioning added per R11) + **H10 Graph app-role grant from `GraphAppRoles.cs` constant** (r3 task 062 landed) | A (for GraphAppRoles.cs GUID completion) | Parallel with A / G. **v3.2**: D20 conditional dropped — r3 owns constant, r1 owns grant. |
| ~~**B' (v3)**~~ | ~~TF Power Platform provider adoption~~ | — | **DEFERRED v3.2 (M-10)** — dev-only reality + 0 customers pending. TF migration lands as its own task chain when first paying customer signs. |
| **C** | Registry schema extension (9 columns) + ProvisioningRun data model (Cosmos, enumerated shapes) + `customer.bicep` extension (Cosmos + optional SignalR; **Redis REMOVED per Q-E FR-12**) + **NEW `uami.bicep` module + `app-service.bicep` UAMI refactor + slot binding + RBAC migration** (v3.2 A3 — structural T5 fix) + `platform.bicep` rebuild (L2 App Service + Cosmos + platform KV) + **L2 control-plane** (REST + AAD B1, App Service B2, concurrency I5, crash-recovery I6, **fire-and-forget + state-reconciler §4.2 v3.2**) integrating all 19 handlers | A, B, G | Core build phase. Includes UAMI migration. |
| **C'** | H12a/b/c config-seed manifest implementation — declarative seed authoritative-source table resolving R14 drift; all seeders idempotent + resumable; H14 integration wiring (2× Exchange policies per T4 with action-and-verify semantics, Graph webhooks, service-endpoint webhooks — S2S consent step REMOVED per r3 task 060). **H12a/b DAG-parallel** per v3.2. | A (drift resolution), C | Highest functional payoff (Gap 1) |
| **C'' (v3.4 NEW — Wave D-1/D-2 per DS-1b §7 + DS-2)** | **Execution engine + Option D ports.** Wave D-1: `ProvisioningHandlerDispatcher` (§4.2b) + freeze test + queue delete/recreate with sessions+dedup (C4.6/C5.4) + SB Receiver RBAC (C5.5) + C4.5 serializer fix + contract/seam tests + EXO sidecar (§4.2a) + 9 thin SDK swaps + H0/H2b/H5/H12a/H12b/H13 ports + H9 artifact re-scope + Path X grant script (§9.6) + `Deploy-ControlPlane.ps1` (C5.9). Wave D-2: H3/H6/H2a heavy ports with parity acceptance tests. **Ordering-critical**: C5.1/C5.2 Bicep config-key fixes land BEFORE any stamp redeploy (the appSettings array fully replaces live settings); C4.5 lands before any dispatcher testing above unit level (a working dispatcher with int-serialized status looks hung). | C | The component GA §C-1.1 identified as never-owned. Phase F re-runs after C''. |
| **D** | `/provision-environment` operator skill + L2 REST API integration + Model 2 consent-capture landing (BFF endpoint per D18) + per-tenant token-metering layer (D19) | C | L3 + fast-follow |
| **E** | `DemoExpirationService` migration + Azure legacy-config deletion + verification | — | Parallel; BFF task, FULL rigor (per CLAUDE.md §10 BFF Hygiene checklist per §5.5 inherited gates). **v3.2**: D20 conditional dropped — r3 task 061 landed `ValidateOnStart` on 24 Tier-1 IOptions; Phase E VERIFIES that discipline is active as a Phase F prerequisite (does not re-implement). |
| **G (v3.2 NEW)** | **Canonical naming compliance at provisioning** (per r3 task 063 handoff §4a — see §7.9). Apply canonical KV secret names in H4 seed; `sprk-{env}-kv` vault as Bicep param; codify `spaarke-spekvcert` DO-NOT-RENAME dev exception in Bicep + config. `naming-conformance-check.ps1` invocation added to H13. **Per owner directive #3**: bake into new-customer provisioning; do NOT remediate live-dev drift. | — | Parallel with A / B; blocks C (bicep vault param needs to exist before H2a builds) |
| **H (v3.2 NEW)** | **#1 KV federation remediation full** (per r3 task 017 assessment §4b + owner directive #3 "not deferred; done in the context of THIS project"). Deliverables: (1) **canonical secret-catalog manifest** (single generated source for seeder + Configure script + tokens doc + Bicep KV secrets) — r3 Phase 3b; (2) **alias collapse** for AI Search key etc. (with §4-mandated Dataverse + live-App-Service pre-check FIRST); (3) **IaC alignment** — bicep secret names + app-setting keys canonical; (4) **runtime `/config.json` fetch** for external-spa + code-pages (closes bake-at-build-time pattern). Coordinate with `ci-cd-unit-test-remediation-r1` for `.github/workflows` gate wiring per r3 `task-042-063-ci-gate-wiring-deferral.md`. | G, C | Substantial scope; adds external-spa + code-pages touch |
| **F** | E2E dry run: fresh **`trial-{yyyymmdd}` customer stamp** (Model 1 profile per H-6) provisioned end-to-end using only the new pipeline; reach `Setup Status = Ready`; validate all 6 §4B silent-fail traps cleared; `naming-conformance-check.ps1` exits 0; Model 1 vs Model 2 differences verified per §4.1a table (both tiers dry-run if reasonable) | C, C', D, E, G, H | Acceptance. Trial-env baseline used because demo/prod decommissioned per R18. |

**Parallelism**: A, B, E, G can start immediately in parallel. C waits on {A, B, G}. C' waits on {A drift resolution, C}. H waits on {G, C}. D waits on C. F is acceptance.

### 14A. Upgrade Model (added v3.3 per Q3) — how existing customers receive updates

**Problem statement**: r1's design v3.2 covered PROVISIONING (standing up a new customer environment from zero) but was silent on UPGRADING (rolling out solution / BFF / config changes to already-provisioned customer environments). These are related but architecturally distinct: provisioning is one-shot green-field; upgrade is repeated brown-field against live customer data. Different failure modes, different rollback semantics, different customer-communication requirements.

This section establishes the upgrade contract. It does NOT introduce new handlers in the r1 catalog (H0–H14); instead it repurposes existing handlers as **upgrade-mode variants** where the semantics differ, and adds explicit upgrade-specific concerns (drift detection, version-compatibility matrix, backwards-compatibility windows) that are silent in the provisioning-only path.

#### 14A.1 The three upgrade classes

| Class | What upgrades | Cadence | Complexity | Rollback |
|---|---|---|---|---|
| **U1 — BFF code** (managed Azure code) | `Sprk.Bff.Api` binaries, config, IOptions changes | Weekly to monthly | Low (slot-swap semantics from Deploy-BffApi.ps1) | Slot-swap reversal within minutes |
| **U2 — Dataverse solutions** (managed schema + web resources) | ~8 shipped solutions per §11.1a; new columns, new option-set values, new plugin steps, new PCF versions, seed-data updates | Monthly to quarterly | Medium — Package Deployer upgrade mode + solution-version dependency graph; irreversible for column removals | No automated rollback; incidents recover via forward-fix |
| **U3 — Bicep infrastructure** (Azure resource drift + Bicep template changes) | Bicep template updates, new resources, SKU changes, region migrations, key rotations | Rare (per release major) | High — real risk of drift-obliteration; requires per-customer maintenance windows | `az deployment group rollback` if within retention window; otherwise manual repair |
| **U1-L2 — control-plane code + sidecar** (v3.4) | L2 App Service binaries via `Deploy-ControlPlane.ps1` (publish → zip-deploy → healthz + queue-property + config-fail-fast verification); EXO sidecar image via ACR tag bump (monthly rebuild cadence: pwsh patch + pinned EXO-module version bump; pin, never `latest`) | L2 code: as needed; sidecar: monthly | Low — L2 is fleet-internal; no customer maintenance window | Redeploy previous artifact / previous ACR tag |

#### 14A.2 Handler reuse — upgrade mode vs first-install mode

r1's H2a/H6/H7/H9/H12a/b/c/H14 handlers all execute in **upgrade mode** when the target `sprk_dataverseenvironment` row already has `sprk_provisionedon` set (not null). Key semantic differences per handler:

| Handler | First-install mode | Upgrade mode |
|---|---|---|
| **H2a** (Bicep infra) | `az deployment group create --mode Incremental` — all resources absent, all get created | Same command; Bicep **detects existing resources by name + skips deploy if unchanged** (per Bicep's built-in idempotency). Drift-detection preflight (`az deployment group what-if`) surfaces any manual resource edits to operator BEFORE apply. |
| **H2b** (AI Search indexes) | 7 canonical indexes PUT to fresh service | Indexes upserted; **new index fields added additively** (Azure Search allows adding filterable/searchable/retrievable fields to existing index without re-index); **breaking field changes (rename, type change, vector dimension change)** require full re-index — flagged as U-CB (breaking) below |
| **H6** (solution import) | `pac solution import --publish-changes --force-overwrite` with dependency order | Same command in **upgrade mode**: Package Deployer detects existing solution + applies upgrade delta + retires the holding solution. Version-bump order matters: dependency solutions (SpaarkeCore) upgrade first. |
| **H4** (KV secrets) | Full seeder run against empty KV | Seeder writes missing secrets; **NEVER overwrites live client secrets unless a rotation is explicitly requested** (v3.3 rotation-safe mode); the 24-month rotation is a separate operator-triggered handler variant (`H4-rotate`) |
| **H7** (env-var values) | 7 canonical values set | Values updated only if changed; H13 verifies clients pick up new values (localStorage cache invalidation: 60-min TTL — accept the window OR force cache-bust via a new value in `sprk_ClientCacheBustToken` env-var, added v3.3) |
| **H12a/b/c** (config-seed) | Full seed of all rows | **Additive-only by default**: new AI action definitions / playbooks / grid configs / field mappings are added; existing rows are LEFT ALONE (customer may have edited them). Explicit `--overwrite-authored-content` flag needed to force-update — reserved for security-critical playbook fixes |
| **H14** (integrations) | 2 Exchange policies + Graph webhook subscriptions created | Existing policies/subscriptions verified; missing ones created (per T4 action-and-verify semantics); no destructive re-create |
| **H9** (BFF deploy) | BFF deployed to production slot | **Blue-green via staging slot**: deploy new build to staging → smoke-test → slot-swap; rollback = re-swap. Coordinate with r3 handoff §6 gates (analyzers-as-errors + god-class ratchet + ArchTests must all pass before slot-swap). Artifact provenance (v3.4): upgrade-mode H9 resolves the artifact by target `{buildId}` from the version-compatibility matrix row; the deployed pair is recorded to `sprk_bffversion`/`sprk_solutionversion` (already §14A.3). |

#### 14A.3 Version compatibility matrix

**The problem**: a customer running BFF v1.5.0 with SpaarkeCore v1.2.3 may not be compatible with a Dataverse solution upgrade that adds a column BFF v1.5.0 doesn't know about. Silent-fail traps galore.

**Solution**: publish a **version compatibility matrix** at each release tag. Two dimensions: **BFF version** × **Solution version**. Each cell = compatibility status:

| Status | Meaning | Upgrade order |
|---|---|---|
| ✅ Green | Compatible; upgrade either first | BFF or solution can lead; doesn't matter |
| 🟡 Yellow | Compatible but MUST upgrade in specific order | E.g., "solution must upgrade first; BFF requires new column X" |
| 🔴 Red | Incompatible; do NOT deploy this pair | Blocked; requires intermediate release |

**Enforcement**: H0 preflight (upgrade mode) reads `sprk_dataverseenvironment.sprk_bffversion` + `sprk_solutionversion`; queries the matrix; **fails the upgrade if the target pair is 🔴 Red**. Adds two new columns to §6.1 registry: `sprk_bffversion` + `sprk_solutionversion` (v3.3 registry extension).

**Matrix publication**: `docs/deployment/version-compatibility-matrix.md` — updated per release tag; source-controlled; queryable by version-string.

#### 14A.4 Breaking change classes (U-CB)

Certain solution/schema/config changes cannot be applied in-place safely and require **coordinated maintenance windows** with customer communication:

- **U-CB-1**: **Column removal or type change** on any `sprk_*` entity — Dataverse allows the change but data is lost/coerced; requires opt-in via `--allow-destructive` flag, mandatory pre-migration data export, customer signoff
- **U-CB-2**: **AI Search index vector dimension change** (e.g., 3072 → 768 embedding-model migration) — requires full re-index; window depends on document volume (hours to days)
- **U-CB-3**: **BFF app-reg permission additions** requiring re-consent from customer admin — customer must click the admin-consent URL again; H0.5 re-consent flow (v3.2) handles this but customer coordination is required
- **U-CB-4**: **SPE container-type schema changes** (rare; typically only on major Microsoft SDK updates) — up-to-24h replication window per T6; treat as maintenance
- **U-CB-5**: **KV secret rotation cascading** to BFF app-restart — client secret rotation invalidates BFF's in-memory MSAL cache; requires slot-swap or App Service restart
- **U-CB-6**: **Client secret expiry** (BFF-API-ClientSecret 24-month) — automated H4-rotate variant + 30-day-out alarm; if missed, BFF authentication breaks silently

Each U-CB has an associated **customer-communication template** in `docs/deployment/customer-comms/` — this is a Phase A deliverable for r1's docs consolidation.

#### 14A.5 Drift detection

Before every U3 (Bicep infra) upgrade, run `az deployment group what-if` to detect operator-modified resources. Options on drift:

- **A**: Accept drift, apply upgrade → resource is reset to declarative state (safe if the manual edit was accidental)
- **B**: Reject upgrade, escalate to customer/operator to reconcile the drift first (safe if the manual edit was intentional and load-bearing)
- **C**: Update Bicep template to match drift, then apply (rare; requires design decision)

**H0 preflight (upgrade mode)** always runs the `what-if`; if drift detected, **defaults to B (reject + escalate)** with drift report emitted as `runNotes/drift-{customerId}-{timestamp}.md`.

#### 14A.6 Upgrade success criteria (added to §15)

- H0 preflight (upgrade mode) blocks incompatible BFF/solution pairs per version matrix
- H2a upgrade-mode drift-detection preflight surfaces manual resource edits BEFORE apply
- H4 upgrade mode is rotation-safe (never overwrites live secrets absent explicit rotate)
- H6 upgrade mode via Package Deployer upgrade-mode with dependency-order respected
- H9 blue-green slot-swap with rollback via re-swap; r3-era gates green before swap
- H12a/b/c upgrade mode is additive-only by default; `--overwrite-authored-content` reserved for security fixes
- U-CB customer-comms templates documented per class
- Version-compatibility matrix at every release tag

#### 14A.7 What this section is NOT

- **Not** a decommission model — see D17 (out of scope; existing `Decommission-Customer.ps1`)
- **Not** a data-migration model — see §11.6 (spaarke-data CLI is separate)
- **Not** a zero-downtime guarantee — the blue-green pattern gives minutes-of-drain during slot-swap; customer-visible URL is uninterrupted but in-flight requests may retry
- **Not** an SLA — customer-facing SLA is separate (customer engagement work, not r1 pipeline concern)

---

## 15. Success Criteria (v3 refreshed 2026-08-12)

**North star**: automated provisioning completes in **<1h of pipeline runtime**; customer is production-ready within **one business day** of admin consent + Azure quota being in place (per PROJECT-UPDATE §9). Three items that blow past a day are lead-time not compute: Azure quota / OpenAI region capacity (1–3 days), SPE container-type replication (up to 24h), customer admin consent + security review (customer-dependent). Front-load lead-time items in preflight. **E2E is achieved via the Option D pipeline** (§4.2/§4.2a/§4.2b): session-serialized dispatch, pure-.NET collaborators, sidecar H14a — a run driven by manual script execution does not satisfy the E2E criterion.

1. One authoritative deployment guide covers all provisioning phases + one validated env-var/app-setting manifest reconciled to BFF code `[Required]` annotations (Gap 4)
2. Each of the 19 handlers (H0…H14) implements `IProvisioningHandler`, is 3-level idempotent per NFR-10, independently testable, reports outcome to the Cosmos run record, and executes pure .NET per §4.1b (H14a via sidecar)
3. The dispatcher consumes `sprk-provisioning-jobs` session-serialized (`SessionId=CustomerId`, `MaxConcurrentCallsPerSession=1`); the reconciler advances the DAG; per-customer serialization is enforced at both admission (I5 guard, Path X creds) and transport (sessions); orphaned runs auto-resume on startup with incremented `attempt`
4. All Gap 3 items — Entra app registration (11 grants), SPE container type (confidential-client fix per T6), Dataverse App User (TF-driven per D14), Model 2 consent-capture (D18) — run unattended and idempotently
5. A brand-new environment reaches `Setup Status = Ready` via the new pipeline; extended `Validate-DeployedEnvironment.ps1` exits 0 asserting end-to-end effects (sample analysis + sample document upload+index + workspace-layout render + wizard field-map)
6. All 6 silent-fail traps (§4B T1–T6) verified cleared by their owning handler's post-condition
7. `DemoProvisioning__Environments__*` and `__DefaultEnvironment` deleted from Azure; expiration flow verified working (R5)
8. `/provision-environment` skill executes the full flow with confirmation gates and produces a handoff report
9. ProvisioningRun records in Cosmos are queryable for fleet status (how many environments, in what state); `sprk_currentrunid` visible on `sprk_dataverseenvironment`
10. All **7 canonical** AI Search indexes (v3.2 authoritative per `scripts/ai-search/Deploy-AllIndexes.ps1` FR-07: files/discovery/records/rag-references/insights/session-files/invoices) created per customer with per-index invariant verifier passing; `spaarke-playbook-embeddings` + `spaarke-knowledge-index` NOT provisioned (retired)
11. All **7** per-customer Dataverse environment variables set and validated (no hardcoded URL fallbacks); reconciled with INVENTORY §9
12. **Model 2** (dedicated per D3): per-customer AI resources (OpenAI, AI Search, Doc Intelligence, Cosmos) deployed isolated; Redis NOT per-customer (v3.2 Q-E FR-12 correction)
13. **Model 1** (trial/SMB per D3): shared fixed-floor tier deployed via `model1-shared.bicep`; per-tenant token-metering layer (D19/A2) enforces `tokenBudgetMonthlyUSD` and blocks pipeline calls when exceeded; per-tenant AI Search filter on every query verified in H13
14. **Cost envelope conforms to pricing model** ([`notes/pricing-research-2026-08-12.md`](notes/pricing-research-2026-08-12.md)) — v3.2 targets updated for Redis-removal: Model 2 empty-environment Azure floor ≤$400/mo (Redis now per-env, so no per-customer P1 delta); Model 1 marginal per-customer ≤$430/mo (5–10 users, capped tokens); shared platform floor for Model 1 ≤$400/mo — deviations >20% flagged in H13 as cost drift
15. **BFF publish size** ≤60 MB compressed (CLAUDE.md §10 NFR-01; net10 baseline 44.96 MB incl PDBs per r3 handoff); Phase E DemoExpirationService migration + Phase C UAMI refactor + H0.5 consent-callback endpoint combined deltas < ~0.5 MB verified per PR
16. **D20 fail-fast config discipline active (r3-landed)** — r3 tasks 060/061/062/017 all landed on master. Verified: (a) BFF misconfig causes `/health` startup failure not runtime failure (r3 task 061); (b) `GraphAppRoles.cs` constant with all 14 GUIDs populated (r3 task 062 + r1 Phase A completion); (c) ArchTests block new IOptions without validation (r3 task 040/042); (d) legacy H4/H10 verification queries retained as safety net
17. **Naming compliance (v3.2 new)**: `scripts/naming-conformance-check.ps1` exits 0 on r1-owned surfaces post-Phase G; canonical vault + secret naming applied at all provisioning entry points (H4 + Bicep param); `spaarke-spekvcert` DO-NOT-RENAME dev exception codified
18. **#1 KV federation Phase H landed (v3.2 new)**: canonical secret-catalog manifest is the single source generating seeder + Configure script + tokens doc + Bicep KV secret set; alias collapse complete (with pre-check evidence in task notes per §7.9 pre-check protocol); external-spa + code-pages consume `/config.json` runtime endpoint (no bake-at-build-time BFF host)
19. **H0 preflight quota check (v3.2 new)**: preflight fails the run BEFORE H1 starts if OpenAI regional TPM, Dataverse env-creation rate, subscription vCPU, or SPE cert-bootstrap show insufficient headroom
20. **§4.2 handler execution model verified (v3.2 new)**: L2 REST endpoint enqueue-and-return-202 confirmed under load test (≥30-min handler completes without HTTP timeout); reconciler correctly advances DAG; queue properties live-verified (`az servicebus queue show` → `requiresSession=true`, `requiresDuplicateDetection=true`); retry-with-`attempt` delivered through dedup; serializer-contract test + scanner seam test green (a `Running` run written by the repository IS returned by `CosmosActiveRunScanner`)
21. **UAMI structural fix landed (Phase C, v3.2 A3)**: `uami.bicep` module created; `app-service.bicep` refactored to consume UAMI + bind to both slots; T5 slot-parity trap intrinsically eliminated (verified by slot-swap smoke test producing no cold-start KV-ref failures)
22. **§5.5 inherited r3-era gates green** (v3.2 M-6): every r1 BFF PR passes analyzers-as-errors + god-class ratchet + 4 new ArchTests + config fail-fast + naming-conformance + Graph-app-role parity ArchTest
23. **Option D runtime landed (v3.4 new)**: main L2 site is a stock code-based deploy (no custom container); EXO sidecar image ≤250 MB compressed, Trivy-gated, non-routable; H14a executes through `IExchangePolicyApplier` → sidecar with T4 action-and-verify semantics preserved; H9 deploys from CI artifact (no provision-time build); Path X live (L2 UAMI systemuser exists on admin env with scoped role; registry writes attributed to it; Path Y Bicep params deleted; `Dataverse-ClientSecret` KV secret intact)

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

**v3.4 resolutions (2026-08-18 Wave A design studies — DS-1/1b, DS-2/2b, DS-5, DS-8)**:

| Q | Question | Resolution | Locked in |
|---|---|---|---|
| **B6** | Handler runtime environment (GA C1.3) | **Option D hybrid**: stock code-deployed L2 App Service, zero shells in main site, SDK/REST collaborators (12 Class-A ports); minimal EXO sidecar sitecontainer for H14a only (~200–230 MB; the sole verified PowerShell-only residual — no Graph API for AAP or App-RBAC successor). Rejected: fat tools container (Option A). | §4.1b + §4.2a |
| **B7** | Dispatcher + per-customer write safety (GA C1.1; DS-2 §2.3 re-examined adversarially in DS-2b vs 5 alternatives) | **Session-serialized dispatch**: `ServiceBusSessionProcessor`, `SessionId=CustomerId`, `MaxConcurrentCallsPerSession=1` (freeze-tested), keyed DI by `HandlerId`; Conflict arms retained (handler∥operator); flip path = conditional-patch append, not ETag-retry. Costs ~4% of E2E wall-clock; zero throughput cost at any scale. | §4.2b |
| **B8** | L2 registry-write credential (GA C1.4 ↔ C5.3/5.6/5.7/5.8) | **Path X**: L2 UAMI as admin-env Dataverse App User, scoped role, `DefaultAzureCredential`; Path Y secrets never provisioned; L2 KV binding deleted, KV secret retained (BFF consumer). Path Z (MI-as-FIC) noted as the r2+ cross-tenant escape hatch. | §9.6 + FR-38 |
| **B9** | H9 build-at-provision defect (DS-1b #19) | **Artifact-based deploy**: CI-published blob by `{buildId}`; provision-time `dotnet publish` forbidden; r3 gates run in CI. | §4.1 H9 row + spec FR-12 |
| **B10** | Queue contract (C4.6/C5.4) + retry survivability | IaC-declared queue with `requiresSession` + `requiresDuplicateDetection` (create-time-only → live delete/recreate); `attempt` field in envelope + MessageId hash so L1 dedup never kills a §4C retry. | §4.2b + §4C + §11.2 |
| **B11** | Run-doc serializer contract (C4.5, #19/#20 family) | Newtonsoft `StringEnumConverter` dual-attributes on `RunStatus`/`GateState`/`QuarantineState`; serializer-contract test + repository→scanner seam test. | §6.2 |

**v3.5 resolutions (2026-08-19, `spaarke-auth-v4-dataverse-MI` change-request coordination)**:

| Q | Question | Resolution | Locked in |
|---|---|---|---|
| **C1** | Model 1 vs Model 2 app-reg shape for the customer BFF's OBO credential (auth-v4 §5.1 DECISION — Reading 1 shared vs Reading 2 per-customer) | **SPLIT — both readings, one per model**: Model 1 = Reading 1 (one shared multitenant app-reg, `AzureADMultipleOrgs`, matches live state); Model 2 = Reading 2 (per-customer app-reg + FIC trusting the shared BFF UAMI). No single global answer — the models' isolation postures already diverge (D3), so the app-reg shape follows. | D2 + §9.1 + §4.1 H3 row + spec.md MUST rules |
| **C2** | R23's 20-FIC-per-app cap — does it bind r1's design? | **No.** Reconciled auth-v4's §4 MI-as-issuer analysis against r1's own Q5-spike framing (which had conflated MI-as-issuer with MI-as-recipient). FIC lives on the trusting app-reg, not the UAMI; each shape needs at most 1–2 of 20. | §12 R23 (CLOSED) |
| **C3** | Tenant-isolation invariant I6 (tenant-isolation namespace — distinct from the v3 "I6 = L2 crash recovery" resolution-code above; disambiguated as "I6 (OBO app-reg)" where both appear in the same context) — adopt auth-v4's §5.4 proposal? | **Adopted, Model 1 only.** Under MI-FIC, Model 1's isolation boundary shifts from resource-level (per-customer KV secret) to code-level (tenant-routed app-reg selection); worth naming and ArchTest-enforcing even though Model 1 has one shared app-reg today. | §4D I6 + spec FR-40 |
| **C4** | Credential-path pluggability during auth-v4's phased rollout (auth-v4 §5.3 CONTRACT) | **Accepted as FR-39.** H3/H4's confidential-credential step supports both secret and FIC without a handler restructure; auth-v4 owns the rollout schedule + secret retirement (Phase 5), not r1. | spec FR-39 |
| **C5** | §9.1 doc contradiction (auth-v4 §5.2 DOC FIX) | **Fixed.** The v3 tenancy-note sentence that read as licensing a Spaarke-owned app-reg with customer-tenant compute is struck through and replaced by the v3.5 tenancy note; the surrounding mechanism (`AzureADMultipleOrgs`, D18, U-CB-3) is preserved, scoped explicitly to Model 1. | §9.1 |

---

## 17. Placement Justification (CLAUDE.md section 10)

- **New scripts + skill + procedure doc**: `scripts/`, `.claude/skills/`, `docs/procedures/` — no BFF impact.
- **Provisioning handlers**: Register in the **control-plane service**, not the BFF. The control plane is Spaarke-internal fleet management (D3, D8, D12); the BFF is per-customer. Zero BFF DI impact.
- **Control-plane service**: New standalone service in `rg-spaarke-platform-{env}`. Not the BFF. Cosmos DB for state. No shared-resource conflict.
- **Only BFF changes** (v3 — up to four additions vs v2, subject to r3 assessment per D20):
  - **Phase E** — `DemoExpirationService` migration (R5 carry-over): modifies an existing registered service to use `DataverseEnvironmentService`. No new endpoints, packages, or DI registrations. Expected publish-size delta: ~0.
  - **Phase D** — **BFF `/api/onboarding/consent-callback` endpoint** (v3, D18) for Model 2 self-service consent capture. NEW endpoint + one new handler. Expected publish-size delta: ~0.1 MB (single controller + verification helper).
  - **Phase E (conditional per D20)** — `[Required]` annotations on 26 `IOptions<T>` classes + `.ValidateDataAnnotations().ValidateOnStart()` middleware. **Skipped if r3 owns as CI gate.** No new endpoints; no new packages; middleware adds ~1 registration. Expected publish-size delta: ~0.
  - **Phase B (conditional per D20)** — Graph app-role compile-time constant + H10 SDK helper reading the constant. **Skipped if r3 owns as ArchTest.** No new endpoints; one new class + one helper. Expected publish-size delta: <~0.05 MB.
  - All changes MUST follow the CLAUDE.md §10 BFF Hygiene checklist: load `.claude/constraints/bff-extensions.md`, publish-size verification (60 MB ceiling), test update obligation, no new HIGH CVEs.
- **Registry schema extension**: Dataverse-only (**9 new columns v3**, was 6 v2 — adds `sprk_currentrunid`, `sprk_tenancymodel`, `sprk_tenantid`).
- **`customer.bicep` extension**: Infrastructure-as-Code only. Adds per-customer AI resources (OpenAI, AI Search, Doc Intelligence, App Insights) **+ Cosmos DB (v3, R11) + optional SignalR (v3)** — no BFF code changes.
- **`platform.bicep` rebuild**: Infrastructure-as-Code only. Shrinks to control-plane resources (L2 App Service, Cosmos, platform KV, monitoring).
- **`model1-shared.bicep`** (v3 §3A A1): first-class trial-tier composition using shared fixed-floor resources (App Service Plan, OpenAI, AI Search) + dedicated for everything else.
- **NEW: Terraform Power Platform provider directory** (v3, `infrastructure/terraform/dataverse/`): separate IaC dialect from Bicep; scoped strictly to Dataverse env + application user lifecycle per §4A.
- **NEW: Per-tenant token-metering layer** (v3, D19): either APIM gateway or app-level custom App-Insights metric keyed on `tenantId`. Placement TBD in D-phase implementation; either way, minimal BFF DI impact (single tracker service).
- **B04 multi-tenant Dataverse routing — documented exception (Path A per CLAUDE.md §6.5)** — `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:62`'s single-URL shape (`configuration["Dataverse:ServiceUrl"]` read once at DI-setup) is **correct-by-design for the shared-BFF pattern**, NOT a Model-1 multi-tenancy defect. Per owner Q1 SESSION 11 (2026-08-26 — BINDING; see `current-task.md` Locked owner decisions + Two-stage E2E model): Model 1 uses **ONE shared Dataverse env per shared BFF app-reg per Azure env**, with multiple *customers* segregated at the data layer (customer records, Business Units, SPE containers) *within* that shared env — NOT via multiple Dataverse environments. The URL is genuinely per-env; there is no runtime cross-tenant DV routing decision being taken. Stage-2 per-customer segregation (SPE containers, search-index params, DV Business Units) is a future r2 customer-onboarding workflow, out of r1 scope. Model 2's `customer.bicep`-provisioned BFF likewise has env=customer 1:1, so its single-URL shape is trivially correct. The §4D I1–I5 invariants enforce logical isolation of the multiple customer records that share one Dataverse env in Model 1. NO code change to `DataverseServiceClientImpl.cs`; NO new ADR amendment; NO new DI seam. Full rationale + rejected alternatives (Path B/Path C) in spec.md §ADR Tensions row for ADR-027+ADR-028 B04. Task 202 punch list row B04; task 204b (this task) formalized the exception in this bullet.

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
- [`notes/pricing-research-2026-08-12.md`](notes/pricing-research-2026-08-12.md) — sourced Azure + M365 pricing (Aug 2026); Model 2 baseline + Model 1 shareable-vs-dedicated segregation; per-tenant allocation math for shared platform floor
- [`discovery/phase-0-discovery-report.md`](discovery/phase-0-discovery-report.md) — original Phase 0 findings

**Load-bearing spine assets** (in-repo):
- `scripts/Provision-Customer.ps1` — 13-step orchestrator (basis for handler catalog)
- `scripts/Build-SpaarkeMaster.ps1` — machine composition of 386-component solution (INVENTORY source of truth)
- `scripts/Deploy-Release.ps1` + `Deploy-Platform.ps1` + `Deploy-BffApi.ps1` + `Decommission-Customer.ps1` + `Validate-DeployedEnvironment.ps1` — release/platform/BFF/teardown/validate
- `scripts/seed-data/Deploy-All-AI-SeedData.ps1` + `Seed-PlaybookConsumers.ps1` + module seeders (H12a/b/c basis)
- `infrastructure/bicep/**` (26 modules + `platform.bicep` / `customer.bicep` / `model1-shared.bicep` / `model2-full.bicep`)
- `scripts/ai-search/Deploy-AllIndexes.ps1` (H2b — 7 indexes)

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

### v3.5 — 2026-08-19 (auth-v4 coordination: MI-FIC adoption for BFF-OBO per ADR-028 A4/E-3. Split spec.md:236 MUST for Model 1 (shared multitenant app-reg) vs Model 2 (per-customer app-reg + FIC). Added invariant I6 (Model 1 only, ArchTest-enforced). Closed R23 with corrected cap analysis. Added FR-39 for pluggable credential path during auth-v4 phased rollout. Doc fix at §1006 contradiction.)

**Trigger**: `spaarke-auth-v4-dataverse-MI` filed `notes/PROVISIONING-CHANGE-REQUEST.md` proposing r1 migrate the customer BFF's confidential OBO credential from a client secret to a federated identity credential (FIC) with Managed Identity as issuer, per ADR-028 Amendment A4 + Exception E-3. r1 had its own investigation (Q5 spike → R23 → DS-8 Path Z) that concluded MI-FIC was Phase-D backlog; auth-v4 wasn't aware of that context. Reconciled both investigations — auth-v4's §4 cap analysis was correct; r1's spike had conflated MI-as-issuer with MI-as-recipient patterns. Owner sign-off 2026-08-19 on the SPLIT below.

**Changes**:

- **D2 + §9.1 tenancy note corrected**: split the Model 1 vs Model 2 app-registration shape. Model 1 (shared trial/SMB) = ONE shared multitenant BFF app registration (`AzureADMultipleOrgs`, matches live state), created once, never per customer. Model 2 (dedicated) = per-customer BFF app registration, retained for tenant-level isolation. Fixed the §9.1 sentence that read as licensing a Spaarke-owned app-reg with customer-tenant compute (a shape explicitly ruled out 2026-08-18) — the surrounding mechanism (`AzureADMultipleOrgs`, D18 consent-callback, U-CB-3 re-consent) is preserved and scoped explicitly to Model 1.
- **§4.1 H3 row split**: Model 1 branch is a no-op for the BFF app-reg (already exists; H0.5/D18 consent-callback captures per-customer trust); Model 2 branch creates a per-customer app-reg + FIC trusting the shared BFF UAMI per auth-v4's §3.1 recipe (issuer/subject=principalId/audience `api://AzureADTokenExchange`; `AADSTS70021` retry logic).
- **NEW spec FR-39**: pluggable secret/FIC credential contract for H3/H4's "configure BFF confidential credential" step — auth-v4 owns the rollout schedule + Phase-5 secret retirement; r1 owns the pluggability + Model 2 FIC creation.
- **NEW invariant I6** (§4D, Model 1 only, spec FR-40): the app registration used for an OBO exchange MUST be derived from per-tenant request context; no default/fallback. ArchTest-enforced (`Spaarke.ArchTests.TenantIsolation.I6_ObApp*`), same pattern as I1–I5. Adopted from auth-v4's §5.4 proposal.
- **§12 R23 CLOSED**: the 20-FIC-per-app cap does not bind either project's shape — FICs live on the trusting app-reg (2/20 Model 1 shared, 1/20 per Model 2 app-reg), not on the UAMI. r1's original Q5 spike (`notes/graph-spe-2026-08-standards-spike.md` §3) conflated MI-as-issuer with MI-as-recipient. No longer a Phase-D backlog item — pulled into r1 scope now via FR-39.
- **§9.6 cross-reference added**: Path X (L2's own admin-env Dataverse credential) explicitly distinguished from auth-v4's BFF-OBO FIC migration (customer BFF's OBO credential) — both use MI-as-issuer but serve different credential stories; do not conflate when reading either project's docs.
- **NEW §16 v3.5 resolutions table** (C1–C5): records the SPLIT decision, R23 closure, I6 adoption, FR-39 pluggability, and the §9.1 doc fix as locked resolutions, mirroring the v3.4 B6–B11 pattern.
- **Tasks amended** (see project's `tasks/` directory): 125 (H4 SDK port — BFF-API-ClientSecret path deprecation note + pluggability contract), 126 (H4 real-value sourcing — FIC-migrated sentinel option), 130 (H3 heavy port — Model 1/Model 2 runtime branch + I6 enforcement), 142 (H7 credential provisioning — env-agnostic sentinel / retirement escalation) — all gain auth-v4-coordination escalation triggers.
- **Response authored**: `notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md` (TL;DR, split rationale, R23 closure, §8 open-item responses, `scripts/` coordination note). `notes/PROVISIONING-CHANGE-REQUEST.md` gets an APPLIED banner.

**Note on the change request's own doc-location references**: the change request cited `spec.md:236` and `design.md:1006`/`:1857` for the MUST rule / doc contradiction / R23 entry respectively; this project's spec.md/design.md had moved on (v3.4, more FRs/sections added) by the time of reconciliation, so the actual line numbers differ from the request's citations (spec.md's MUST rule is now ~line 249; the §9.1 contradiction is now ~line 1076; the authoritative R23 entry is §12 line ~1515, not the §20 v3.3-changelog mention at ~1857). Applied at the correct current locations; content intent unchanged.

### v3.4 — 2026-08-18 (Wave A design-study integration)

Root fix: §4.2 step 2 / spec FR-22 previously placed handler execution "in the BFF's existing `IJobHandler` infrastructure" — contradicting D8/D12, the MUST rules, and the implementation, and leaving the queue consumer unowned (the direct cause of Phase F closing without E2E; GA §C-1.1). v3.4 corrects the execution model to L2-owned Option D and integrates the six companion locked decisions. Changes: §4A tooling table rewritten for SDK/REST execution; §4.1 preamble contract naming (`IProvisioningHandler`); NEW §4.1b runtime classification; §4.1 H9 artifact re-scope; §4.2 concurrency two-halves + corrected execution model; NEW §4.2a runtime topology (stock App Service + EXO sidecar); NEW §4.2b session dispatcher + keyed resolution + queue contract; §4B T4 sidecar note; §4C `attempt` retry envelope; §5.1/§5.4 terminology + flip path; §6.2 serialization contract; §9.2 cross-ref + NEW §9.6 Path X; §9A row 15; §11.2 queue/sidecar IaC rows; §11.3 dispositions resolved; §14 Phase C''; §14A U1-L2 + H9 provenance; §15 SC 2/3/20 updates + SC 23 + north-star clause; §16 B6–B11. Evidence: notes/design-study-ds1b, ds2, ds2b, ds5, ds8, ds6 + r1-gap-analysis-2026-08-18.

### v3.3 — 2026-08-16 (owner-review round Q1–Q7 + Q5 Graph/SPE spike)

**Trigger**: owner reviewed v3.2 and raised 7 substantive questions/clarifications. Q5 explicitly authorized a research spike. Each Q resolved with a design addition or clarification.

**Additions**:

- **§5.4 EXPANDED** (Q1 custom state machine vs Durable Functions vs Temporal): full trade-off table for each of Option A (custom, chosen), Option B (Durable Functions, rejected), Option C (Temporal, rejected). Reasoning: single-digit runs/day cadence means fixed cost of workflow product > marginal cost of custom state machine. Migration story if wrong: swap L2 orchestration without changing handler contracts.
- **NEW §4.3a Claude Code Operator Toolchain** (Q4): 15-row tool matrix (PowerShell / Bash / WebFetch / az / pac / Dataverse MCP / Azure MCP + fallbacks); auth flow (operator's own AAD identity, NOT service principal; `az account get-access-token --resource api://spaarke-provisioning-controlplane-{env}`); operator machine prerequisites (pwsh 7.4+, az 2.60+, pac 1.35+, git 2.40+); Phase D deliverable spec for `/provision-environment` skill; fallback matrix for MCP disconnects (real concern given our own experience 2026-08-14/15)
- **NEW §4D Tenant Isolation Invariants** (Q6): 5 binding invariants (I1 no hardcoded default tenant in scripts; I2 unconditional `tenantId` filter on AI Search; I3 partition-key predicate on Cosmos; I4 SPE container IDs tenant-scoped-derived; I5 Graph token per-tenant scoped) with enforcement mechanism (mostly new ArchTests per r3 task 040 pattern) + verification query + why-it-matters (severity per invariant — CATASTROPHIC for I2/I4/I5 legal-privilege leak scenarios). Threat model: honest-but-buggy code + operator error, NOT malicious insider or external actor.
- **NEW §9A Consolidated Identity + Config Surface** (Q7): 14-row single-page table showing per-customer artifacts (BFF app-reg, UAMI, Dataverse App User, KV, KV secrets, env-vars, IOptions, Graph roles, Exchange policies, webhook keys, SPE container, tenant `tid`, token budget) — where each lives, who provisions, who verifies, rotation cadence, Model 1 vs Model 2 differences. One-page mental model at bottom: **Model 2 = 1 BFF app-reg + 1 UAMI + 1 Dataverse env with 2 App Users + 1 KV with ~18 secrets + 1 SPE container + 14 Graph app-roles + 2 Exchange policies + 7 env-vars + 2 webhook keys + 1 tid**.
- **NEW §11.1a Solutions Reconciliation** (Q2): resolves the 36-in-src / 8-in-deployer / 3-in-src-dataverse-solutions confusion. **Authoritative 8 solutions** (SpaarkeCore + webresources + 6 feature solutions) per Deploy-DataverseSolutions.ps1 `$SolutionImportOrder`. Other ~28 in `src/solutions/` are code pages deployed as web resources via `Deploy-Release.ps1` Phase 4 (not solutions). Reconciles `~10 solutions` misreport across INVENTORY §1, §1 Executive Summary, §11.1, PROJECT-UPDATE §2. Phase A audit obligation to enumerate each of the ~28.
- **NEW §14A Upgrade Model** (Q3): three upgrade classes (U1 BFF code / U2 Dataverse solutions / U3 Bicep infra) with cadence/complexity/rollback per class; per-handler upgrade-mode semantics table (H2a/H2b/H4/H6/H7/H9/H12a/b/c/H14 all differ); version compatibility matrix (BFF × Solution version cells: green/yellow/red); breaking-change classes (U-CB-1 through U-CB-6 — column removal, vector dimension change, permission additions, SPE schema, KV secret cascading, client-secret expiry); drift detection via `az deployment group what-if` before every U3 upgrade with default REJECT + escalate behavior; upgrade success criteria; explicit non-goals (not decommission, not data migration, not zero-downtime SLA).
- **Risk register R22 (v3.3)**: Exchange ApplicationAccessPolicy → RBAC for Applications migration watch (Q5 spike found this coming but no hard cutover date; coexistence safe; add Phase D backlog item for r2 consideration)
- **Risk register R23 (v3.3)**: MI-as-Federated-Identity-Credential opportunity for Model 2 secretless cross-tenant Graph app-only (GA'd 2026; 20-FIC-per-app cap; not needed for r1's current design but material Phase C+ optimization if adopted)
- **§4.1 H8 footnote** (v3.3): SPE `FileStorageContainerType.Manage.All` no longer requires SPE-Admin / Global-Admin as of June 2026 per Q5 spike (runbook simplification, not code change)

**Code fix (not design)**:
- **`scripts/Register-EntraAppRegistrations.ps1:63` — hardcoded Spaarke tenant DEFAULT removed** (v3.3 tenant-isolation invariant I1 enforcement). `[string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"` → `[Parameter(Mandatory=$true)] [string]$TenantId`. Doc comments updated; examples updated. Prevents cross-tenant provisioning accidents.

**Q5 research spike deliverable**:
- **NEW `notes/graph-spe-2026-08-standards-spike.md`**: full report from researcher. TL;DR: Graph SDK v6.5.0 / Kiota 2.0 latest (no v7); SPE patterns all still current; Terraform Power Platform provider v4.1.0 Jan 2026 validates D14. Verdict: **v3.2 has zero stale patterns blocking execution**; three follow-on items now folded into design (R22, R23, H8 footnote).

**Companion doc updates**:
- Researcher's own memory: `.claude/agent-memory/researcher/MEMORY.md` + new pinned memory `graph-spe-standards-2026-08-16.md`

**Ready for `/design-to-spec`**: no external blocking dependencies. Owner sign-off on the v3.3 additions completes the design engagement.

### v3.2 — 2026-08-15 (post-r3-completion + Fable-verified + net10 baseline)

**Trigger**: r3 completed 2026-08-14 (tasks 060/061/062/017 all landed on master + net10 cutover); this branch merged `origin/master` (commit `41bacbdae`) resolving MEMORY.md conflict + inheriting all r3-era forcing-functions. Owner directed a Fable-model adversarial review to ensure customer provisioning process is solid + 100% accurate + efficient before proceeding.

**r3-handoff resolutions**:
- **D20 PENDING → LANDED**: r3 tasks 060 (S2S drop) / 061 (ValidateOnStart) / 062 (`GraphAppRoles.cs`) / 017 (KV federation assessment) all landed. §14 Phase B + E conditionals dropped. r1 residual work is (a) H10 grants roles from `GraphAppRoles.cs` constant + syncs UAMI SP; (b) complete 11 of 14 null `AppRoleId` GUIDs; (c) H4 leverages r3 ValidateOnStart as primary defense (H4 verification queries retained as safety net).
- **§9.1 Dataverse S2S App Reg REMOVED** (r3 task 060 dropped it — zero code consumers)
- **§7.7 KV secrets** — dropped `Dataverse-S2S-ClientId` + `Dataverse-S2S-ClientSecret`; added `AiSearch--AdminKey` canonical (was 3 aliases); added `Dataverse-ServiceUrl` canonical (was `SPRK-DEV-DATAVERSE-URL` with env token baked in); added `cosmos-endpoint`
- **§4.1 H3** — 14 grants (not "~11"); ONE app-reg only (not 2); consent-callback for Model 2 self-service
- **§4.1 H14** — sub-step (d) S2S consent removed; (a)/(b)/(c) marked DAG-parallel with action-and-verify semantics per T4
- **§4B T3** — updated to reference `GraphAppRoles.cs` as source of truth; noted 10/14 GUID gap as r1 completion obligation
- **NEW Phase G**: Canonical naming compliance at provisioning (per r3 task 063 handoff §4a; owner directive #3 — bake into new provisioning, skip live-dev remediation)
- **NEW Phase H**: #1 KV federation remediation full — canonical secret-catalog manifest (r3 Phase 3b as r1 deliverable), alias collapse with pre-check protocol, IaC alignment, external-spa + code-pages runtime `/config.json` fetch (owner directive #3 — "not deferred; done in the context of THIS project")
- **NEW §7.9**: KV-Secret & Resource Naming Compliance section — the 4 canonical rules + reference syntax + r1 rename map from r3 task 063 handoff
- **NEW §5.5**: Inherited gates from r3-era master — analyzers-as-errors, god-class ratchet, 4 new ArchTests, config fail-fast, publish size, Graph v6/Kiota 2.0 error types

**Fable-verified corrections** (Fable H-1, H-2, H-3, M-1, M-3, M-4, M-7):
- **H-1 fixed**: `Deploy-AllIndexes.ps1` path corrected to `scripts/ai-search/` (was `infrastructure/ai-search/` in 5+ places)
- **H-2 fixed**: §8.2 rewritten from `Deploy-AllIndexes.ps1` `$Catalog` variable (authoritative). 7 canonical: files/discovery/records/rag-references/insights/session-files/invoices. `spaarke-playbook-embeddings` explicitly retired; `spaarke-knowledge-index` archived; `spaarke-discovery-index` ACTIVE (v3.1 wrongly said "dropped"); `spaarke-files-index` plural (was singular). Retired index sections in §8.3 flagged as DO-NOT-REFERENCE.
- **H-3 corrected**: §9.2 rewritten — UAMI is aspirational (no `uami.bicep` module exists); current pattern is System-Assigned MI. Phase C absorbs the migration (new module + `app-service.bicep` refactor + RBAC migration + Graph app-role migration + Dataverse App User re-registration). §7.2 UAMI row marked as Phase C target with interim caveat.
- **M-1 fixed (Q-E FR-12)**: Redis is per-environment, not per-customer. Removed from §7 catalog (row 6 → REMOVED); removed from §7.1 naming table; removed from H2a scope in §4.1 handler catalog; added `Deploy-RedisCache.ps1` to §11.1 as REUSE (per-env).
- **M-2 acknowledged**: §11.1 `Provision-Customer.ps1` disposition changed from "PORT" to "PORT + MAJOR EXTEND" with the 6 new module invocations enumerated (not just "add Cosmos")
- **M-3 fixed**: `cosmos.bicep` → `cosmos-db.bicep` (actual filename)
- **M-4 fixed**: 26 → 25 Bicep modules (the 26th was a `.json` lifecycle policy)
- **M-7 fixed**: All Dataverse S2S artifact references removed across §9.1, §7.7, §4.1 H3, §4.1 H14

**Missing scenarios absorbed** (Fable §6 items 1/2/3/7 per owner decision):
- **§4C NEW — Rollback semantics on partial failure**: 4-class failure taxonomy (Resumable / Retryable-with-cleanup / Quarantine-required / Successful-but-drifted); Cosmos state transitions incl. `Quarantined`; new `POST /api/runs/{id}/clear-quarantine` endpoint
- **§4.2 handler execution model spelled out (Fable M-9)**: fire-and-forget via Service Bus + state-reconciler `BackgroundService` in L2; addresses App Service 230s HTTP timeout vs 30-min handlers
- **§4.1 H0 quota preflight extended**: OpenAI regional TPM + Dataverse env-creation rate + subscription vCPU + SPE cert-bootstrap checks; blocks the run before H1 starts (surfaces §9 north-star lead-time items UP-FRONT)
- **§4.1a NEW — Model 1 vs Model 2 handler behavior differences table**: 8 handler rows enumerating per-tier differences (H0/H2a/H2b/H4/H7/H10/H12c/H13); trial-environment strategy (Model 1 `trial-{yyyymmdd}` for Phase F E2E per H-6)

**Deferrals per v3.2**:
- **M-10 TF Power Platform provider adoption DEFERRED** to first-customer engagement — dev-only reality, 0 customers pending; H5/H10 use interim `pac admin` + PPAC + Graph SDK path. D14 remains the design target.
- **Phase F acceptance target**: trial-environment (Model 1) provisioned via r1 pipeline (per H-6 decision) — not demo/prod re-provisioning (demo/prod decommissioned for budget per r3 CLAUDE.md)

**Handler catalog additions**:
- **H0 (extended)**: quota preflight checks
- **H0.5 (extended)**: re-consent semantics for existing environments
- **H2a (major-extend)**: 6 new module invocations (Cosmos + OpenAI + AI Search + Doc Intel + App Insights + optional SignalR); Redis removed; UAMI via Phase C
- **H4 (extended)**: canonical naming applied at seed; consumes Phase H manifest
- **H14**: sub-step (d) S2S removed; sub-steps (a)/(b)/(c) parallelized with action-and-verify semantics

**Risk register additions**: R17 (KV naming drift), R18 (dev-only baseline), R19 (cross-customer concurrency limits), R20 (handler execution model + HTTP timeout), R21 (UAMI migration debt)

**Success criteria**: 22 total (was 16); new #17 (naming compliance), #18 (KV federation Phase H landed), #19 (H0 quota preflight), #20 (execution model verified), #21 (UAMI structural fix), #22 (§5.5 inherited gates green)

**Scope**: 30 in-scope items (was 21); adds Phase G (naming), Phase H (KV federation), Phase C (UAMI), §4C (rollback), §4.2 (execution model), H0 (quota preflight), §4.1a (M1/M2 differences), `GraphAppRoles.cs` completion

**Ready for `/design-to-spec`**: no further external blocking dependencies. Owner review of v3.2 recommended before running spec pipeline.

### v3.1 — 2026-08-12 (D20 + r3 handoff for #1/#3)

**Trigger**: owner feedback surfaced deployment complexity concerns around app-registration proliferation + env-var/config plumbing. Analysis identified 4 refactors (#1 KV federation, #2 fail-fast config validation, #3 app-reg consolidation via UAMI federated credentials, #4 Graph app-role parity via code constants). Cross-referenced against `code-quality-and-assurance-r3` (on-hold BFF quality program) which already owns the axis for #3 as NG1 and has natural homes for #2/#4 as forcing functions.

**Changes**:
- **§3 D3**: rewritten IN PLACE (no longer "no shared resources ever"; describes both Model 2 dedicated + Model 1 shared trial/SMB directly). Eliminates the v2 "rule says X but §3A adds Y" reader-confusion pattern.
- **§3A**: reframed from "ADR-Tensions D3 Path A Amendment" to "D3 Two-Tier Rationale" — economic *why*, not rule *what*.
- **§3 D20 (NEW)**: locked decision for fail-fast config validation + Graph app-role code constants — **status PENDING r3 assessment**. r1 pauses after design.md complete; r3 decides whether these land in r1 Phase E or r3 forcing-functions.
- **§4B trap catalog**: T1/T2/T3 updated with "Post-D20" notes explaining fail-at-deploy vs fail-at-runtime semantics.
- **§14 phasing**: Phase B + Phase E gained conditional D20 tasks (skipped if r3 owns discipline).
- **§15 success criteria**: added #16 (D20 discipline active + verifiable) and updated #14 for cost envelope.
- **§17 placement justification**: expanded from 2 BFF changes to up to 4 (conditional on r3 assessment outcome).
- Companion doc: [`notes/pricing-research-2026-08-12.md`](notes/pricing-research-2026-08-12.md) added with Model 1 §5B shareable-vs-dedicated segregation.
- **Handoff to r3**: [`ask doc`](../code-quality-and-assurance-r3/notes/deployment-complexity-refactors-ask-2026-08-12.md) written; #1 KV federation + #3 app-reg consolidation are r3's decision to accept/defer. r3 evaluates whether to bring NG1 in-scope for #3.
- **r1 pause**: r1 is paused pending r3 assessment. When r3 completes, r1 resumes and confirms/updates D20 + Phase B/E accordingly, then runs `/design-to-spec` → `/project-pipeline`.

### v3 — 2026-08-12 (post-assessment refresh)

**Trigger**: project paused June 2026; owner assessment 2026-08-12 (PROJECT-UPDATE-2026-08-12.md + COMPONENT-INVENTORY.md) surfaced 6 items v2 missed and 1 locked decision (D3) worth re-validating.

**Changes**:
- **Header**: Draft v3, companion-doc references, revision line for 2026-08-12
- **§3**: D14 rewritten (TF Power Platform provider replaces PPAC semi-auto fallback); D18 added (BFF as consent-capture onboarding client); D19 added (per-tenant token-metering as no-regret investment)
- **§3 D3 rewritten in place** (v3): now describes both Model 2 (dedicated) and Model 1 (shared trial/SMB) tiers directly, rather than v2's "no shared resources ever" formulation
- **§3A NEW**: D3 Two-Tier Rationale — the *why* behind the two-tier design (economic analysis, three supporting decisions A1/A2/A3, references to pricing research). Deliberately not framed as an "amendment" to avoid the reader-confusion pattern of "the rule says X but a footnote elsewhere adds Y"
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
- **§15**: 15 success criteria (added trap-verified, 7-index/env-var reconciliation, Model 1 + Model 2 both verified, **cost-envelope conformance to pricing model with drift detection**, publish-size compliance, north-star framing)
- **§16**: v3 resolutions table (B1–B5, I1–I3, I5–I6, D3-tension, Tooling, Self-service)
- **§17**: Two BFF changes now (consent-callback endpoint added); 9-column registry (was 6); Model 1 stack + TF + metering layer placement
- **§18**: Open items 6–10 added (INVENTORY §12 verification backlog + TF maturity + trap verification)
- **§19 NEW**: References — companion docs, spine assets, guides to consolidate, related projects, ADR anchors
- **§20 NEW**: this CHANGELOG

**What did NOT change**:
- 3-layer architecture shape (L1 handlers + L2 control plane + L3 skill)
- ADR-004 resolution (§5.1) — individual handlers implement the L2-local `IProvisioningHandler` contract (ADR-004-shaped); L2 orchestrates
- ADR-010/017 posture (§5.2/5.3) — control plane's DI is separate from BFF's; ProvisioningRun ≠ per-handler job status
- D1 (managed solutions), D2 (two targets), D4 (subscription per customer), D5 (Spaarke buys licenses), D6 (B2B vs Native identity), D7 (consumption SKUs), D8 (build L1→L2→L3), D9 (Claude Code as authorized MCP client), D10 (gates verified not inferred), D11 (idempotent + resumable), D12 (control plane placement), D13 (Cosmos as run store), D15 (hybrid profiles), D17 (decommission out of scope)
- **D3 changed shape**: v2 = "no shared resources ever"; v3 = "two tiers, Model 2 dedicated / Model 1 shared floors + logical isolation" — see §3A rationale

### v2 — 2026-06-16 (feedback round 1)
Resource inventory, identity spec, config capture, Q1–Q6 resolved → D12–D17 locked.

### v1 — 2026-06-15 (initial draft)
Superseded `spaarke-environment-factory-r1` design; captured Phase 0 discovery + D1–D11.

---

*End of design specification v3. Next step: owner review, then `/design-to-spec` → `/project-pipeline`.*
