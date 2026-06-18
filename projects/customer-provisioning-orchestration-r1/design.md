# Customer Provisioning & Deployment Orchestration — Design Specification

> **Status**: Draft v2 — post-feedback revision, pending final owner review
> **Created**: 2026-06-15
> **Revised**: 2026-06-16 (feedback round 1: resource inventory, identity spec, config capture, Q1-Q6 resolved)
> **Author**: Ralph Schroeder / Claude Code
> **Project**: customer-provisioning-orchestration-r1
> **Supersedes**: `projects/spaarke-environment-factory-r1/design.md`
> **Predecessors**: `spaarke-environment-provisioning-app` (r1, complete PR #390), Phase 0 discovery report (`discovery/phase-0-discovery-report.md`)

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
| D14 | **Dataverse app user creation = semi-automated with manual gate.** Attempt Dataverse SDK `ServiceClient.Create` on `systemuser`; fall back to `advance_gate` if headless path fails. Gate verification via `systemusers` query is fully supported. | H10 handler attempts automation; operator clears gate via PPAC UI if needed. |
| D15 | **Hybrid environment profiles.** Named profiles (`spaarke-hosted`, `customer-owned`, `demo`, `trial`) set default parameter bundles; every parameter individually overridable. Preflight validates final parameter set. | Profiles are shorthand, not constraints. |
| D16 | **L3 orchestration via operator skill + Dataverse MCP for reads.** `/provision-environment` skill handles sequencing and gates (like `/deploy-new-release`). Existing Dataverse MCP tools handle data operations. No separate MCP server in r1. | Simplest viable L3; MCP server deferred to when MDA dashboard or Assistant needs arise. |
| D17 | **Decommission out of scope.** Existing `Decommission-Customer.ps1` remains operational as-is. Registry-aware teardown handlers deferred to r2. | No decommission handlers in this project. |

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
| H7 | Dataverse environment variables (8) + BFF app settings | Step 8 + deploy-time token substitution | -- | `envvars-{customerId}-v{n}` |
| H8 | SPE container type + root container | `Create-NewContainerType.ps1` | (async token propagation wait) | `spe-{customerId}` |
| H9 | BFF deploy + app settings | `Deploy-BffApi.ps1` + `auth-deployment-setup.md` | -- | `bff-{customerId}-v{buildId}` |
| H10 | Dataverse application user (UAMI) | Manual today (PPAC) — semi-automated (D14) | **App user exists** (Dataverse query) | `appuser-{customerId}` |
| H11 | User provisioning (identity preset) | r1 registration flow (D6) | **B2B consent** (B2BGuest only) | `users-{customerId}` |
| H12 | Config + starter-data seeding | Per-module seed scripts (scope-limited) | -- | `seed-{customerId}-v{n}` |
| H13 | Validation gate | `Validate-DeployedEnvironment.ps1` (exit 0/1) | **Validation passed -> registry Ready** | `validate-{customerId}-v{n}` |
| H14 | Post-deploy integration wiring | NEW | -- | `integrations-{customerId}` |

### 4.2 Layer 2 — Control-Plane Service (D12)

The orchestration layer that sequences handlers, manages run state, enforces gates, and exposes APIs to front ends.

**Placement**: Standalone service (new App Service or Container App) in `rg-spaarke-platform-{env}`. Not in the per-customer BFF — the control plane manages the fleet. `platform.bicep` is rebuilt to deploy only control-plane resources (compute, Cosmos DB, platform Key Vault, monitoring). Per-customer AI resources (OpenAI, AI Search, Doc Intelligence) move to `customer.bicep` per D3.

**State store**: Cosmos DB serverless (`spaarke-provisioning` database, `runs` container), per D13. Sub-10ms writes, JSON-native for `CompletedPhases`, `GateStates`, `InterStepState`.

**API surface** (consumed by L3 front ends):

| Endpoint / Tool | Purpose |
|------|---------|
| `create_provisioning_run` | Initialize a run against an environment record |
| `run_preflight` | Execute H0, return parameter validation results |
| `get_run_status` | Return current phase, completed phases, gate states |
| `advance_gate` | Operator marks a manual gate as cleared (e.g., H10 app user) |
| `resume_run` | Resume a failed run from the failure point |
| `get_phase_logs` | Return logs/output for a specific phase |
| `cancel_run` | Cancel an in-progress run |

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

The r1 entity has 16 columns deployed. Extend with provisioning infrastructure fields:

| Schema Name | Type | Purpose |
|---|---|---|
| `sprk_azuresubscriptionid` | Text(100) | Azure subscription hosting this environment |
| `sprk_resourcegroupname` | Text(200) | Resource group |
| `sprk_appservicename` | Text(200) | BFF App Service |
| `sprk_keyvaultname` | Text(200) | Key Vault |
| `sprk_containertypeid` | Text(100) | SPE container type |
| `sprk_provisionedon` | DateTime | When validation first passed |

### 6.2 ProvisioningRun — Cosmos DB Serverless (D13)

One execution of the pipeline against a target. Multiple runs per environment over time (initial provision, re-provision, repair).

**Database**: `spaarke-provisioning`
**Container**: `runs` (partition key: `/customerId`)

| Field | Type | Purpose |
|---|---|---|
| id | string (GUID) | Unique run identifier |
| customerId | string | Partition key + customer reference |
| environmentId | GUID | Lookup -> `sprk_dataverseenvironment` |
| status | string | NotStarted, Running, WaitingOnGate, Completed, Failed, Cancelled |
| currentPhase | integer | Current handler (0-14) |
| completedPhases | array | Completed phase numbers with timestamps |
| gateStates | object | Per-gate verification results |
| parameters | object | Run parameters (see Section 6.6) |
| interStepState | object | Data passed between handlers (entraUserId, systemUserId, etc.) |
| profile | string | Environment profile used (spaarke-hosted, customer-owned, demo, trial) |
| attemptCount | integer | Number of resume attempts |
| createdAt | datetime | Run creation |
| completedAt | datetime | Run completion (success or final failure) |
| errorDetail | string | Last error message |
| ttl | integer | Auto-expire after 365 days (Cosmos TTL) |

**Fleet visibility**: Future web app reads from Cosmos directly. No Dataverse sync needed in r1 — the `sprk_dataverseenvironment` entity provides fleet-level status via `Setup Status` field (already deployed).

---

## 7. Azure Resource Specification (Per-Customer)

Every customer environment deploys a dedicated, isolated set of Azure resources per D3. This section is the authoritative inventory for what H2 (infrastructure handler) must provision.

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
| 2 | **Key Vault** | `key-vault.bicep` | Standard | RBAC auth, soft delete 90d, purge protection, MI gets Secrets User role |
| 3 | **Storage Account** | `storage-account.bicep` | Standard_LRS | TLS 1.2, blob public access disabled, 3 containers (see 7.3) |
| 4 | **Service Bus** | `service-bus.bicep` | Standard | TLS 1.2, 4 queues (see 7.3), 5-min lock, 14-day TTL, DLQ enabled |
| 5 | **Redis Cache** | `redis.bicep` | Basic C0 (no VNet) / Premium P1 (VNet) | TLS-only (port 6380), allkeys-lru eviction |
| 6 | **App Service Plan** | `app-service-plan.bicep` | S1 (Standard) | Linux |
| 7 | **App Service (BFF)** | `app-service.bicep` | — | .NET 8.0, HTTPS-only, always-on, HTTP/2, system-assigned MI, health check `/health` |
| 8 | **Azure OpenAI** | `openai.bicep` | S0 | 4 model deployments (see 7.4) |
| 9 | **AI Search** | `ai-search.bicep` | Standard | Semantic search enabled, 8 indexes (see Section 8) |
| 10 | **Document Intelligence** | `doc-intelligence.bicep` | S0 | prebuilt-layout model (see 7.5) |
| 11 | **App Insights + Log Analytics** | `monitoring.bicep` | PerGB2018 | 90-day retention, resource permissions enabled |
| 12 | **Content Safety** (optional) | `content-safety.bicep` | S0 | West US 2 or East US 2 only (Prompt Shields requirement) |
| 13 | **AI Foundry Hub + Project** (optional) | `ai-foundry-hub.bicep` | Basic | Prompt Flow orchestration, attached to storage + KV + insights |

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

### 7.6 Deployment Order

1. Resource Group
2. Log Analytics + App Insights (monitoring, referenced by others)
3. Key Vault (secrets storage, created early so other modules can store outputs)
4. Storage Account
5. Service Bus
6. Redis Cache
7. App Service Plan
8. OpenAI Service
9. AI Search (+ index creation via REST API/SDK — see Section 8)
10. Document Intelligence
11. App Service (BFF, depends on plan + KV + all AI service endpoints)
12. Content Safety (optional)
13. AI Foundry Hub + Project (optional)

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

### 8.1 Index Naming Standard

**Convention**: `spaarke-{subject}-{qualifier}` where `{subject}` identifies the data domain and `{qualifier}` distinguishes index variants when needed.

### 8.2 Active Index Inventory (8 Indexes)

All production indexes use **3072-dimensional vectors** with `text-embedding-3-large`, HNSW algorithm (m=4, efConstruction=400, efSearch=500, cosine metric), and semantic ranking.

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

**Deprecated**: `spaarke-knowledge-index-v2` (dual-vector 1536+3072) — replaced by `spaarke-knowledge-index` with 3072-only vectors. `discovery-index` (1536-dim prototype) — not actively referenced in main BFF code.

### 8.3 Index Field Specifications

**NOTE**: Full field-level schemas are defined in the JSON files referenced above. The following summarizes key fields and vector configuration per index. A detailed field-by-field audit should be performed against the JSON schemas during Phase A to confirm all fields align with current BFF service usage.

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

### 8.5 Index Provisioning (H2 Post-Step)

After Bicep deploys the AI Search service, indexes must be created via REST API / Azure SDK. Handler H2 or a sub-step of H2 applies each index JSON schema definition. The 8 JSON schema files in `infrastructure/ai-search/` are the source of truth.

**Action item for Phase A**: Audit the 8 JSON schema files against current BFF service field usage. Confirm field-level alignment. Standardize any naming inconsistencies.

---

## 9. Identity & Access Specification

### 9.1 Entra App Registrations (2 Per Customer)

#### BFF API App Registration

| Property | Value |
|----------|-------|
| Display Name | `spaarke-bff-api-{customerId}-{env}` |
| Sign-in Audience | AzureADMyOrg (single tenant) |
| Platform | Web |
| App ID URI | `api://{bff-app-id}` |
| Client Secret Expiry | 24 months |
| Redirect URI | `https://{api-domain}/.auth/login/aad/callback` |
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

**Type**: System-assigned MI on App Service (created by Bicep `app-service.bicep`).

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

**Graph App Roles (on MI service principal, granted via Graph API):**

| Permission | Type | Purpose |
|------------|------|---------|
| FileStorageContainer.Selected | App role | SPE container access |
| Files.ReadWrite.All | App role | SPE file operations |
| User.Read.All | App role | User lookups |
| Group.Read.All | App role | Group membership checks |
| Mail.Send | App role | App-only mail sending |
| Mail.Read | App role | Email module ingestion |
| MailboxSettings.Read | App role | Mailbox settings |

### 9.3 Dataverse Security

**Application Users (2, created in H10):**

| Principal | Security Role | Business Unit | Method |
|-----------|--------------|---------------|--------|
| BFF app registration (by app ID) | System Administrator | Root | Semi-automated (D14): attempt SDK, fallback PPAC UI |
| MI service principal (by MI app ID) | System Administrator | Root | Same |

**Custom Security Roles (shipped in SpaarkeCore solution):**

| Role | Audience | Permissions |
|------|----------|-------------|
| Spaarke User | All end users | Read/create/write to Spaarke entities |
| Spaarke AI Analysis User | Users running analyses | Read documents, create analyses, view results |
| Spaarke AI Analysis Admin | Administrators | All user + manage playbooks, configure AI settings |

Roles are defined in the managed solution and imported by H6. User assignment is post-provisioning (customer admin task).

### 9.4 Exchange Online Application Access Policies (Optional)

Required only if Communication/Email modules are enabled.

**Mail-enabled security group**: `Spaarke Email Access` (`spaarke-central-email@{customer-tenant}`)

**Two ApplicationAccessPolicy objects:**
1. BFF app registration → `RestrictAccess` scoped to group
2. MI service principal → `RestrictAccess` scoped to group

**Propagation**: Up to 30 minutes before Graph mailbox calls succeed.

### 9.5 Webhook Security

| Webhook | Signing Key Secret | Algorithm | Header | Endpoint |
|---------|-------------------|-----------|--------|----------|
| Communication (Graph subscriptions) | `communication-webhook-signing-key` | HMAC-SHA256 | `X-MSHUB-Signature` | `/api/communications/incoming-webhook` |
| Email (Dataverse service endpoint) | `Email-WebhookSigningKey` | HMAC-SHA256 | `Authorization-Context` | `/api/v1/emails/webhook-trigger` |

Both secrets are 48-byte base64, generated during H4, fail-closed if missing.

---

## 10. Parameter Model & Customer Configuration

### 10.1 Environment Profiles (D15)

Named profiles set default parameter bundles. Every parameter is individually overridable. Preflight (H0) validates the final parameter set.

| Profile | Bicep Stack | Identity Preset | Subscription Target | Default SKUs | Notable Gates |
|---------|------------|----------------|-------------------|-------------|---------------|
| `spaarke-hosted` | model2-full | B2BGuest | SpaarkeOwned | S1/Standard | Lighthouse: skip |
| `customer-owned` | model2-full | NativeAccount | CustomerOwned | Customer-specified | Lighthouse: required |
| `demo` | model2-full (reduced) | B2BGuest | SpaarkeOwned | B1/Basic/Free | Lightweight validation |
| `trial` | model2-full (time-limited) | B2BGuest | SpaarkeOwned | B1/Basic | Expiry date gate |

### 10.2 Run Parameters

**Required (7):**

| Parameter | Type | Constraints | Purpose |
|-----------|------|------------|---------|
| `customerId` | string | 3-10 chars, lowercase alphanumeric | Resource naming, partition key |
| `displayName` | string | Human-readable | Dataverse environment display name |
| `tenantId` | GUID | Valid Entra tenant | Auth authority, env vars |
| `clientId` | GUID | Service principal | PAC CLI and Admin API auth |
| `clientSecret` | string | Or `certificateThumbprint` | PAC CLI auth |
| `bffApiAppId` | GUID | BFF app registration | Env var `sprk_BffApiAppId` |
| `msalClientId` | GUID | Typically same as bffApiAppId | Env var `sprk_MsalClientId` |

**Optional (with defaults):**

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `profile` | `spaarke-hosted` | Environment profile (sets other defaults) |
| `bffApiBaseUrl` | `https://api.spaarke.com` | Env var `sprk_BffApiBaseUrl` |
| `azureOpenAiEndpoint` | (empty) | Env var `sprk_AzureOpenAiEndpoint` |
| `shareLinkBaseUrl` | (empty) | Env var `sprk_ShareLinkBaseUrl` |
| `environmentName` | `prod` | Resource naming suffix |
| `location` | `westus2` | Azure region |
| `dataverseRegion` | `unitedstates` | Power Platform region |
| `platformKeyVaultName` | `sprk-platform-prod-kv` | Control-plane KV (shared secrets) |
| `platformResourceGroup` | `rg-spaarke-platform-prod` | Control-plane RG |
| `resumeFromStep` | 0 (auto-detect) | Resume from specific handler |
| `skipDataverse` | false | Skip H5-H8 (when Dataverse env already exists) |

### 10.3 Dataverse Environment Variables (8)

Set by H7. Queried at runtime by PCF controls and Code Pages via `environmentvariabledefinition` + `environmentvariablevalue` entities. 5-minute in-memory cache + 60-minute localStorage persistence.

**No migration to Azure App Configuration has occurred** — Dataverse environment variables remain the canonical client-side configuration mechanism. The `environmentVariables.ts` utility (`src/client/pcf/shared/utils/`) is the single retrieval point.

| Schema Name | Display Name | Purpose | Source Parameter |
|---|---|---|---|
| `sprk_BffApiBaseUrl` | BFF API Base URL | Backend API endpoint (normalized: no trailing slash, no `/api` suffix) | `bffApiBaseUrl` |
| `sprk_BffApiAppId` | BFF API App ID | OAuth scope audience | `bffApiAppId` |
| `sprk_MsalClientId` | MSAL Client ID | MSAL public client ID for Dataverse-hosted SPAs | `msalClientId` |
| `sprk_TenantId` | Tenant ID | Azure AD tenant (MSAL authority) | `tenantId` |
| `sprk_AzureOpenAiEndpoint` | Azure OpenAI Endpoint | AI features endpoint | `azureOpenAiEndpoint` |
| `sprk_SharePointEmbeddedContainerId` | SPE Container ID | Document storage container | Populated by H8 (SPE provisioning) |
| `sprk_ApplicationInsightsKey` | Application Insights Key | Client-side telemetry | App Insights deployment output |
| `sprk_DefaultPlaybookId` | Default Playbook ID | Default AI playbook GUID | Seed data (H12) |

**Critical rule**: All 8 variables are required in production. Client components fail at startup with clear configuration error if any variable is missing or empty. No hardcoded URL fallbacks (per task 024 rationale: prevents silent breakage).

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

Verified against the codebase 2026-06-15. Legend: **PORT** = logic feeds a handler; **REUSE** = consumed as-is; **REBUILD** = concept kept, form changes; **NEW** = does not exist.

### 11.1 Scripts and Orchestration

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `Provision-Customer.ps1` | `scripts/` | **PORT** | 13 steps -> handler catalog. State-file resume -> ProvisioningRun record. |
| `Deploy-DataverseSolutions.ps1` | `scripts/` | **REUSE** | Called by H6. Confirm managed-capable (D1). |
| `Deploy-BffApi.ps1` | `scripts/` | **REUSE** | Called by H9. |
| `Validate-DeployedEnvironment.ps1` | `scripts/` | **REUSE** | Called by H13. Exit code -> gate state. |
| `Test-Deployment.ps1` | `scripts/` | **REUSE** | Smoke-test handler. |
| `Register-EntraAppRegistrations.ps1` | `scripts/` | **PORT** | Basis for H3. Needs full idempotency (creation + 5 permission grants + admin consent). |
| `Create-NewContainerType.ps1` | `scripts/` | **PORT** | Basis for H8. Must encode westus-billing + token-propagation-wait workarounds. |
| `Decommission-Customer.ps1` | `scripts/` | **OUT OF SCOPE** (D17) | Remains operational as-is. Registry-aware teardown deferred to r2. |
| `Deploy-Release.ps1` + `/deploy-new-release` | `scripts/`, `.claude/skills/` | **REUSE as-is** | Out of scope. Reference model for L3 skill UX. |

### 11.2 Infrastructure-as-Code

| Asset | Path | Disposition | Notes |
|---|---|---|---|
| `customer.bicep` | `infrastructure/bicep/` | **REUSE + EXTEND** | Extend for dedicated OpenAI/Search/DocIntel/AppInsights per D3/D12. |
| `platform.bicep` | `infrastructure/bicep/` | **REBUILD** | Shrinks to control-plane-only: L2 compute, Cosmos DB, platform KV, monitoring (D12). |
| 18 Bicep modules | `infrastructure/bicep/modules/` | **REUSE** | Composable building blocks. |
| model1/model2 stacks | `infrastructure/bicep/stacks/` | **ASSESS** | `model2-full.bicep` is the canonical per-customer template. Reconcile in Phase C. |

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

**Location**: `C:\code_files\spaarke-data-cli`
**Status**: Pre-alpha scaffolding (269 lines TypeScript, 2 commits, zero implementation).
**Relevance**: H12 (seeding) maps to this CLI's `load` command long-term. The `onboard` command (Phase 3) is the eventual customer data import pipeline.
**Design decision**: H12 stays thin in this project (config + minimal starter data via existing PowerShell seed scripts). The CLI's value is medium-term (demo quality, customer onboarding) and is a separate project dependency.

---

## 12. Risk Register

Absorbed from the 13 known deployment-guide issues + r1 carry-overs. Verified against codebase 2026-06-15.

| ID | Risk / known issue | Source | Design must... |
|---|---|---|---|
| R1 | SPE container-type creation requires SPO Management Shell (not Graph), billing must use `westus`, and 10-30 min Graph token-propagation wait. | ENV-GUIDE issue 8, 9, 13 | Make H8 idempotent + async-wait-aware; encode region + wait workarounds; expose as verified gate, not fixed sleep. |
| R2 | Dataverse application user creation is PPAC-UI-only (no documented API/CLI). | ENV-GUIDE issue 11 | H10 attempts SDK headless path; falls back to manual-by-design gate (D14). |
| R3 | Solution export/fix pipeline is 8 manual sed-style steps; managed-vs-unmanaged changes it (D1). | ENV-GUIDE section 6 | H6 scripts export->fix->pack-managed->verify; no manual edits. |
| R4 | Entra app reg = 5 permission GUIDs granted by hand; no recovery script. | ENV-GUIDE section 4 | H3 scripts grants idempotently; admin-consent is a verified gate (D10). |
| R5 | `DemoExpirationService` still binds `[Obsolete]` `DemoProvisioningOptions.Environments`/`DefaultEnvironment`; blocks deleting Azure config. | r1 lessons | Carry-over: migrate to registry lookup. Not critical path but tracked. |
| R6 | Doc drift: `auth-azure-resources.md` names retired `spe-api-dev-67e2xz`; `spaarke-data-cli/environments.yaml` has same stale URL. | r1 lessons + data-cli review | Fix during doc consolidation; handlers read names from config/registry, never hardcode. |
| R7 | "Validated but not wired" defect class (r1 FR-11: license parsed, never applied). | r1 lessons | Every handler's acceptance asserts value reaches its consumer. H13 checks effects, not intentions. |
| R8 | CORS localhost leakage, missing ChatModelName, max-upload-size < PCF bundle, solution import order, canvas-app deps. | ENV-GUIDE issues 1-7, 10 | Fold each into relevant handler's post-conditions + validation script. |
| R9 | AI Search index schema drift: JSON schema files may not match current BFF field usage after multiple feature projects. | Feedback round 1 | Phase A must audit all 8 index JSON schemas against BFF service code. |

---

## 13. Scope

### In Scope

1. **L1 handler catalog** — 15 handlers (H0-H14) implementing the provisioning pipeline as idempotent `IJobHandler` implementations
2. **L2 control-plane service** — standalone service with Cosmos DB state, run lifecycle, gate management
3. **L3 operator skill** — `/provision-environment` Claude Code skill using Dataverse MCP for data ops + skill for sequencing (D16)
4. **ProvisioningRun data model** — Cosmos DB `spaarke-provisioning` database with `runs` container (D13)
5. **Registry extension** — 6 new columns on `sprk_dataverseenvironment` for infrastructure references
6. **Gap automation scripts** — Entra app registration (H3), SPE container type (H8), solution export/fix managed pipeline (H6)
7. **Managed-solution packaging** — scripted export/fix/pack-managed/verify pipeline (D1)
8. **Parameter model** — hybrid profiles (D15) + full parameter spec (Section 10.2)
9. **AI Search index provisioning** — 8 indexes created per customer with standard naming (Section 8)
10. **Documentation consolidation** — one canonical procedure superseding the 3-generation documentation
11. **`platform.bicep` rebuild** — shrink to control-plane-only resources (D12)
12. **`customer.bicep` extension** — add per-customer OpenAI, AI Search, Doc Intelligence, App Insights (D3/D12)
13. **E2E dry run** — stand up one brand-new environment using only the new pipeline

### Out of Scope

- Demo/customer **data-seeding tooling** beyond config + minimal starter data (`spaarke-data-cli` is separate; H12 stays thin)
- **Multi-tenant architecture** changes (`spe-multi-tenant-architecture-r1`)
- Changes to the **ongoing release process** (`/deploy-new-release` consumed as-is)
- **Disaster recovery / backup** automation
- **L3 fleet web app** (acknowledged, built later per D13)
- **Spaarke Assistant** front end (design-acknowledged, built later)
- **CI/CD workflow changes** (existing workflows consumed as-is; handlers call underlying scripts directly)
- **Registry-aware decommission pipeline** (D17; existing `Decommission-Customer.ps1` remains operational as-is, deferred to r2)

### Carry-Overs (tracked, not critical path)

- R5: `DemoExpirationService` migration off `[Obsolete]` options -> registry lookup
- R6: Doc drift fixes (`auth-azure-resources.md`, `spaarke-data-cli/environments.yaml`)
- r1 live-provisioning sign-off (criteria 5/8/9/11) folded into E2E dry run

---

## 14. Phasing

| Phase | Content | Depends on | Notes |
|---|---|---|---|
| A | Canonical procedure doc + Gen-1 guide supersession + doc-drift fixes + **AI Search index schema audit** (R9) | -- | Parallel with B |
| B | Gap automation scripts (Entra apps, SPE container type, solution export/fix managed) — independently testable | -- | Parallel with A |
| C | Registry schema extension + ProvisioningRun data model (Cosmos) + `customer.bicep` extension (per-customer AI resources) + `platform.bicep` rebuild + control-plane orchestrator integrating handlers | A, B | Core build phase |
| D | `/provision-environment` operator skill + L2 API integration | C | L3 |
| E | `DemoExpirationService` migration + Azure legacy-config deletion + verification | -- | Parallel; BFF task, FULL rigor |
| F | E2E dry run: new environment end-to-end + r1 live sign-off items + wrap-up | C, D, E | Acceptance |

---

## 15. Success Criteria

1. One procedure doc covers all 15 provisioning phases with a single automation entry point or explicit manual-by-design marking per phase
2. Each handler (H0-H14) is idempotent, independently testable, and reports its outcome to the Cosmos run record
3. The control plane sequences handlers, manages gates, and supports resume-from-failure
4. Entra app registration, SPE container type, and solution export/fix run unattended and idempotently
5. A brand-new environment reaches `Setup Status = Ready` via the new pipeline; `Validate-DeployedEnvironment.ps1` exits 0
6. `DemoProvisioning__Environments__*` and `__DefaultEnvironment` deleted from Azure; expiration flow verified working
7. `/provision-environment` skill executes the full flow with confirmation gates and produces a handoff report
8. ProvisioningRun records in Cosmos are queryable for fleet status (how many environments, in what state)
9. All 8 AI Search indexes created per customer with standardized naming and verified field alignment
10. All 8 Dataverse environment variables set and validated (no hardcoded URL fallbacks)
11. Per-customer AI resources (OpenAI, AI Search, Doc Intelligence) deployed isolated from other customers (D3 verified)

---

## 16. Resolved Design Decisions

These questions were identified in the Phase 0 discovery report, confirmed unresolved during the 2026-06-15 resource review, and resolved in the 2026-06-16 feedback round. Decisions are incorporated into the locked decisions (D12-D17) and design sections above.

| Q | Question | Resolution | Locked Decision |
|---|----------|-----------|----------------|
| Q1 | Control-plane placement & fate of `platform.bicep` | **Standalone service** (new App Service or Container App) in platform RG. `platform.bicep` shrinks to control-plane-only. Per-customer AI resources move to `customer.bicep`. | D12 |
| Q2 | ProvisioningRun store | **Cosmos DB serverless** (`spaarke-provisioning` database, `runs` container). Fleet UI is a future web app, not MDA dashboard in r1. | D13 |
| Q3 | Headless Dataverse application-user creation (H10) | **Semi-automated with manual gate.** Attempt Dataverse SDK `ServiceClient.Create`; fall back to `advance_gate` if headless path fails. Gate verification via `systemusers` query. | D14 |
| Q4 | Decommission scope | **Out of scope.** Existing `Decommission-Customer.ps1` remains operational. Deferred to r2. | D17 |
| Q5 | Environment profiles vs pure parameters | **Hybrid profiles.** Named profiles set defaults; every parameter overridable. Preflight validates final set. | D15 |
| Q6 | MCP server runtime & auth | **No separate MCP server in r1.** `/provision-environment` skill handles orchestration (like `/deploy-new-release`). Existing Dataverse MCP tools handle data ops. MCP server deferred to when additional L3 front ends are needed. | D16 |

---

## 17. Placement Justification (CLAUDE.md section 10)

- **New scripts + skill + procedure doc**: `scripts/`, `.claude/skills/`, `docs/procedures/` — no BFF impact.
- **Provisioning handlers**: Register in the **control-plane service**, not the BFF. The control plane is Spaarke-internal fleet management (D3, D8, D12); the BFF is per-customer. Zero BFF DI impact.
- **Control-plane service**: New standalone service in `rg-spaarke-platform-{env}`. Not the BFF. Cosmos DB for state. No shared-resource conflict.
- **Only BFF change**: `DemoExpirationService` migration (R5 carry-over) — modifies an existing registered service to use `DataverseEnvironmentService`. No new endpoints, packages, or DI registrations. Expected publish-size delta: ~0.
- **Registry schema extension**: Dataverse-only (6 new columns on existing entity).
- **`customer.bicep` extension**: Infrastructure-as-Code only. Adds per-customer AI resources (OpenAI, AI Search, Doc Intelligence, App Insights) — no BFF code changes.
- **`platform.bicep` rebuild**: Infrastructure-as-Code only. Shrinks to control-plane resources.

---

## 18. Open Items for Phase A Audit

These items require detailed verification during Phase A (documentation consolidation) before implementation begins:

1. **AI Search index schema audit**: Compare all 8 JSON schema files in `infrastructure/ai-search/` against current BFF service field usage. Confirm field names, types, vector dimensions, and filterable/searchable attributes match. Flag any schema drift (R9).
2. **Document Intelligence feature verification**: Confirm `prebuilt-layout` is the only model in use. Verify `DocumentParserRouter` file-type routing is complete and accurate. Check if any custom models are planned.
3. **BFF app settings completeness**: Verify the 26 `IOptions<T>` configuration sections (Section 10.4) against `appsettings.template.json`. Confirm all deploy-time tokens are documented. Identify any settings that should move from literal values to Key Vault references.
4. **Dataverse environment variable usage**: Confirm all 8 variables (Section 10.3) are actively consumed by client code. Verify no additional variables have been added by recent projects. Confirm no migration to Azure App Configuration is in progress.
5. **Index naming standardization**: The dev environment uses `spaarke-invoices-dev` (non-standard suffix). Standardize to `spaarke-invoices-index` or confirm the `-dev` suffix is intentional for dev-only isolation.

---

*End of design specification. Next step: owner review of this v2 document, then `/design-to-spec` -> `/project-pipeline`.*
