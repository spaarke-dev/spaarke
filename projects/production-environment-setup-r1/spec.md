# Production Environment Setup R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-11
> **Source**: design.md (v1.0)

## Executive Summary

Deploy Spaarke to production using a hybrid architecture — shared platform resources (BFF API, AI services, monitoring) deployed once, with per-customer resources (Storage, Key Vault, Service Bus, Redis, Dataverse) provisioned on demand. The project delivers Bicep infrastructure templates, deployment automation scripts, CI/CD pipelines, a comprehensive naming standard, and operational documentation. The demo environment serves as the first "customer" to validate the complete provisioning process end-to-end.

## Scope

### In Scope

**Domain A: Bicep Restructuring**
- Create `platform.bicep` (shared resources) from existing reusable modules
- Create `customer.bicep` (per-customer resources) from existing reusable modules
- Create parameter files: `platform-prod.bicepparam`, `demo-customer.bicepparam`, `customer-template.bicepparam`
- Validate all Bicep with `az deployment group what-if` before deploying
- Retain existing Model 1 and Model 2 templates for reference

**Domain B: Platform Deployment (Shared Resources)**
- Deploy shared platform to `rg-spaarke-platform-prod` in `westus2`
- App Service (P1v3) with autoscaling, deployment slots, custom domain (`api.spaarke.com`) + SSL
- Azure OpenAI with production model deployments (GPT-4o, GPT-4o-mini, text-embedding-3-large)
- AI Search Standard2 with 2 replicas
- Document Intelligence S0
- Monitoring (App Insights + Log Analytics, 180-day retention)
- Platform Key Vault (`sprk-platform-prod-kv`) with shared secrets

**Domain C: Customer Provisioning Automation**
- `Provision-Customer.ps1` — end-to-end: Azure resources → Key Vault → Dataverse env (via Admin API) → solution import → SPE containers → smoke tests
- `Decommission-Customer.ps1` — clean teardown of all per-customer resources
- Customer registration in BFF API tenant registry

**Domain D: BFF API Deployment**
- Parameterize `Deploy-BffApi.ps1` for any environment (currently hardcoded to dev)
- Deploy via staging slot → health check → swap to production (zero-downtime)
- Configure `appsettings.Production.json` with Key Vault references
- Create `api.spaarke.com` custom domain with Azure-managed SSL

**Domain E: Dataverse Solution Deployment**
- `Deploy-DataverseSolutions.ps1` — import all managed solutions in dependency order
- PAC CLI authentication per environment
- Import order: SpaarkeCore → webresources → feature solutions (10 total)
- Automate Dataverse environment creation via Power Platform Admin API

**Domain F: Demo Environment (First Customer)**
- Customer ID: `demo`, deployed using same scripts as real customers
- Dedicated Dataverse environment (automated via Admin API)
- Spaarke-hosted SPE containers with sample documents
- B2B guest access for demo users, non-confidential test data
- Validates every step of the provisioning process

**Domain G: CI/CD Pipeline**
- `deploy-platform.yml` — manual dispatch for platform infrastructure
- `deploy-bff-api.yml` — trigger on push to master (paths: `src/server/api/**`), staging → swap → rollback
- `provision-customer.yml` — manual dispatch with customer parameters
- Environment protection rules (staging approval, production approval)

**Domain H: Resource & Component Naming Standard**
- Two standard prefixes: `sprk_` (Dataverse/code) and `spaarke` (Azure resources)
- Complete naming matrix for: Azure resources, Dataverse components, Entra ID registrations, code namespaces, Service Bus queues, Redis keys
- Finalize `AZURE-RESOURCE-NAMING-CONVENTION.md` from "Proposed" to "Adopted"
- Dev environment stays as-is (legacy names); production starts clean

**Domain I: Operational Documentation**
- Production deployment guide (step-by-step)
- Customer onboarding runbook
- Incident response procedures
- Secret rotation procedures
- Monitoring and alerting setup guide

### Out of Scope

- SPE multi-tenant Graph auth — separate project (`spe-multi-tenant-architecture-r1`)
- Self-service registration app — separate project, consumes provisioning APIs built here
- Customer-hosted AI resources — future project
- Release management at scale (ring-based deployment) — future when >20 customers
- VNet/Private endpoints — handled by `production-performance-improvement-r1` (Tasks 040-046)
- Data migration — no existing production data
- Power Platform licensing procurement — business/legal decision
- Disaster recovery / backup strategy — separate workstream

### Affected Areas

- `infrastructure/bicep/` — New `platform.bicep`, `customer.bicep`, parameter files
- `scripts/` — New provisioning, deployment, decommission scripts
- `.github/workflows/` — New deployment workflows
- `src/server/api/Sprk.Bff.Api/appsettings.Production.json` — Production config with Key Vault refs
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` — Tenant configuration service (minimal)
- `src/solutions/` — Ensure all build to managed solution ZIPs
- `infrastructure/README.md`, `docs/guides/` — Deployment guides, runbooks
- `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` — Finalize to "Adopted"

## Requirements

### Functional Requirements

1. **FR-01: Platform Deployment** — A single Bicep template (`platform.bicep`) MUST deploy all shared platform resources to `rg-spaarke-platform-prod` in `westus2` with production SKUs, using parameterized values for environment name, region, and capacity. Acceptance: `az deployment group create` succeeds, all resources operational.

2. **FR-02: Customer Provisioning** — `Provision-Customer.ps1` MUST create all per-customer Azure resources, create Dataverse environment via Power Platform Admin API, import all managed solutions, register customer in BFF API, and provision SPE containers — requiring only customer ID and display name as inputs. Acceptance: Script completes <30 min, smoke tests pass.

3. **FR-03: Customer Decommissioning** — `Decommission-Customer.ps1` MUST cleanly remove all per-customer resources (Azure resource group, Dataverse environment, SPE containers) without affecting other customers or the shared platform. Acceptance: All customer resources deleted, platform unaffected.

4. **FR-04: BFF API Deployment** — `Deploy-BffApi.ps1` MUST deploy to any environment (dev, staging, production) using deployment slots with zero-downtime swap. Parameterize resource group, app service name, health check URL. Acceptance: `/healthz` returns 200 after swap, no downtime during deployment.

5. **FR-05: Dataverse Solution Import** — `Deploy-DataverseSolutions.ps1` MUST import all 10 Spaarke managed solutions to a target Dataverse environment in dependency order (SpaarkeCore first). Acceptance: All solutions imported, no import errors.

6. **FR-06: Demo Parity** — Demo environment MUST be deployed using identical scripts and templates as real customers (no special-casing). Acceptance: Demo deployed via `Provision-Customer.ps1 -CustomerId demo`.

7. **FR-07: Health Verification** — Every deployment MUST include automated verification (health checks, connectivity tests, smoke tests) via `Test-Deployment.ps1`. Acceptance: Smoke test script validates BFF API, Dataverse, SPE, and AI services connectivity.

8. **FR-08: Secret Management** — All credentials MUST be in Azure Key Vault. App Service MUST use Key Vault references (`@Microsoft.KeyVault(...)`). No secrets in app settings, Bicep parameters, or source code. Acceptance: Zero secrets in plaintext config.

9. **FR-09: CI/CD Pipeline** — GitHub Actions workflows MUST exist for: (a) platform infrastructure, (b) BFF API build/test/deploy, (c) customer provisioning. All with environment protection rules. Acceptance: BFF API auto-deploys on push to master via staging slot.

10. **FR-10: Idempotent Deployment** — All deployment scripts and Bicep templates MUST be idempotent — running multiple times with same parameters produces same result. Acceptance: Deploy platform twice consecutively, no errors.

11. **FR-11: Naming Convention** — All new production resources MUST follow the adopted naming standard (`sprk_`/`spaarke-` patterns). Acceptance: Every resource name matches the naming matrix in the design document.

12. **FR-12: Monitoring** — All production resources MUST report to shared App Insights. BFF API logs MUST include `customerId` dimension. Acceptance: Customer-filtered log queries return results.

13. **FR-13: Custom Domain** — BFF API MUST be accessible at `api.spaarke.com` with Azure-managed SSL certificate. Acceptance: HTTPS request to `https://api.spaarke.com/healthz` returns 200.

14. **FR-14: Dataverse Environment Automation** — `Provision-Customer.ps1` MUST create Dataverse environments programmatically via Power Platform Admin API (not manual admin center). Acceptance: Script creates environment, waits for provisioning, returns URL.

### Non-Functional Requirements

- **NFR-01: Provisioning Time** — Full customer provisioning (Azure + Dataverse + solutions + SPE) MUST complete in under 30 minutes.
- **NFR-02: Zero-Downtime Deploy** — Production BFF API deployments MUST use deployment slots for zero-downtime swap.
- **NFR-03: Customer Isolation** — A single customer's resource failure MUST NOT impact other customers or the shared platform.
- **NFR-04: Cost Transparency** — Platform and per-customer costs MUST be trackable separately via resource group separation and Azure tags.
- **NFR-05: Audit Trail** — All deployments MUST be logged (who, what, when, which environment).
- **NFR-06: Rollback** — BFF API MUST support rollback via slot swap. Dataverse solutions MUST support version rollback.

## Technical Constraints

### Applicable ADRs

- **ADR-001** (Minimal API + BackgroundService): Single App Service deployment, no Azure Functions. Health check at `/healthz`. Service Bus for async work.
- **ADR-004** (Async Job Contract): Service Bus queues must support at-least-once delivery. Handlers must be idempotent.
- **ADR-009** (Redis-First Caching): Azure Cache for Redis required. Short TTLs for security data. No hybrid L1+L2 without profiling.
- **ADR-010** (DI Minimalism): Use Options pattern with `ValidateOnStart()`. ≤15 non-framework DI registrations.
- **ADR-017** (Async Job Status): Job status persistence in Dataverse. 202 Accepted with status URL.
- **ADR-018** (Feature Flags): Typed options classes for flags. 503 ProblemDetails when disabled.
- **ADR-019** (ProblemDetails): ProblemDetails for all errors. Correlation ID in all logs. No PII leakage.
- **ADR-020** (Versioning): SemVer for packages. Tolerant readers for evolving payloads.

### MUST Rules

- ✅ MUST deploy BFF API as single App Service (no Azure Functions) — ADR-001
- ✅ MUST expose `/healthz` endpoint for monitoring — ADR-001
- ✅ MUST use Service Bus for all async work (not in-process queues) — ADR-001, ADR-004
- ✅ MUST configure Service Bus for at-least-once delivery with idempotent handlers — ADR-004
- ✅ MUST use Redis for distributed caching (not in-memory only) — ADR-009
- ✅ MUST store all secrets in Azure Key Vault with Key Vault references — FR-08
- ✅ MUST use deployment slots for zero-downtime deploys — NFR-02
- ✅ MUST follow `sprk_`/`spaarke-` naming standard for all resources — FR-11
- ✅ MUST use managed solutions (not unmanaged) for Dataverse — design requirement
- ❌ MUST NOT deploy Azure Functions or Durable Functions — ADR-001
- ❌ MUST NOT store secrets in app settings, Bicep parameters, or source code — FR-08
- ❌ MUST NOT use legacy naming prefixes (`spe-*`, `sdap-*`) for new resources — FR-11

### Existing Patterns to Follow

- See `infrastructure/bicep/modules/` for reusable Bicep modules (app-service, redis, service-bus, etc.)
- See `infrastructure/bicep/model2-full.bicep` for full-stack resource provisioning pattern
- See `scripts/Deploy-BffApi.ps1` for existing BFF API deployment (needs parameterization)
- See `.github/workflows/sdap-ci.yml` for existing CI pipeline structure
- See `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` for naming patterns

## Success Criteria

1. [ ] Platform Bicep deploys all shared resources without errors — Verify: `az deployment group create` exits 0
2. [ ] Customer Bicep deploys per-customer resources without errors — Verify: `az deployment group create` exits 0
3. [ ] BFF API responds at `https://api.spaarke.com/healthz` — Verify: HTTP 200 response
4. [ ] Demo Dataverse environment created via Admin API with all solutions — Verify: PAC CLI `solution list` shows all 10
5. [ ] Demo SPE containers functional — Verify: Upload + download test document succeeds
6. [ ] Demo AI services work — Verify: RAG query returns results, document analysis succeeds
7. [ ] `Provision-Customer.ps1` onboards second test customer — Verify: Full smoke test passes
8. [ ] `Decommission-Customer.ps1` cleanly removes test customer — Verify: Resource group deleted, platform unaffected
9. [ ] GitHub Actions deploys BFF API via staging slot — Verify: Zero-downtime swap observed
10. [ ] All secrets in Key Vault, none in plaintext — Verify: Audit app settings, Bicep params
11. [ ] All resources follow naming standard — Verify: Compare against naming matrix
12. [ ] Deployment guide tested by second person — Verify: Independent deployment succeeds
13. [ ] Smoke test suite runs in <5 min — Verify: `Test-Deployment.ps1` execution time

## Dependencies

### Prerequisites

- Azure subscription with sufficient quota (OpenAI, AI Search, App Service P1v3)
- Entra ID admin access in tenant `a221a95e-...` for new app registrations
- Power Platform admin access for Dataverse environment provisioning via Admin API
- GitHub repository admin for Actions secrets and environment protection rules
- DNS access for `api.spaarke.com` A/CNAME record
- Existing Bicep modules in `infrastructure/bicep/modules/` (all present)

### External Dependencies

- Microsoft Graph API (SPE operations — beta endpoints)
- Power Platform Admin API (Dataverse environment creation)
- Azure Resource Manager API (Bicep deployments)
- PAC CLI (Dataverse solution import)
- GitHub Actions (CI/CD runner infrastructure)

### Related Projects

- **production-performance-improvement-r1** — Infrastructure hardening (VNet, autoscaling, Redis) layers on top. Tasks 040-046 should execute after this project's Phase 2.
- **spe-multi-tenant-architecture-r1** — Multi-tenant Graph auth for per-customer SPE. This project uses single-tenant Spaarke-hosted SPE for demo.
- **spaarke-self-service-registration-app** — Consumes provisioning APIs built here. Runs after Phase 4.

### Execution Order

```
1. production-environment-setup-r1 (THIS PROJECT)
   ├── Phases 1-3: Platform + demo
   ├── 2. production-performance-improvement-r1 (parallel, infra tasks)
   ├── 3. spe-multi-tenant-architecture-r1 (after Phase 2)
   └── Phases 4-5: Customer provisioning + CI/CD
       └── 4. spaarke-self-service-registration-app (after Phase 4)
```

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Entra ID tenant | Same tenant as dev or separate production tenant? | **Same tenant** (`a221a95e-...`) | Reuse existing tenant. New app registrations with production-specific names. B2B guest config shared. |
| API domain | What domain for production BFF API? | **`api.spaarke.com`** | Configure DNS A/CNAME, Azure-managed SSL, CORS in PCF controls and code pages. |
| Dataverse provisioning | Manual admin center or automated? | **Automate via Power Platform Admin API** | `Provision-Customer.ps1` creates Dataverse environments programmatically. Must handle Admin API limitations (license assignment, provisioning wait). |
| Azure region | Same as dev (westus2) or different? | **`westus2` (same as dev)** | Simplest option. All resource types available. No cross-region concerns. |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **GitHub Actions runners**: Assuming GitHub-hosted runners (simpler, no maintenance). Can switch to self-hosted later if VNet access needed.
- **SSL certificate**: Assuming Azure-managed free certificate (auto-renewal, less operational burden).
- **Cost budget**: Assuming ~$1,025-1,255/month for platform + demo is acceptable. Will optimize if needed.
- **Dataverse environment URL**: Assuming `spaarke-demo.crm.dynamics.com` (or closest available).
- **Solution rollback**: Assuming Dataverse managed solution upgrade/rollback is sufficient (no separate backup mechanism).
- **Power Platform Admin API availability**: Assuming current API supports environment creation with required configuration. May need manual fallback for edge cases.

## Unresolved Questions

*Non-blocking — can be resolved during implementation:*

- [ ] Power Platform Admin API: Can it set Dataverse environment variables and security roles during provisioning, or is PAC CLI needed for post-creation config? — Impacts: Script Step 7 automation level.
- [ ] Managed solution export: Are all 10 solutions currently exporting as managed ZIPs via `pac solution export --managed`? — Impacts: Whether solution packaging needs work before deployment script.
- [ ] Service Bus queue names: Does the BFF API read queue names from config (overridable) or are they hardcoded? — Impacts: Whether naming migration (`sdap-jobs` → `document-processing`) requires code changes.

---

*AI-optimized specification. Original design: design.md (v1.0)*
