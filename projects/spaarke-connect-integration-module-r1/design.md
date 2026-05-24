# Spaarke Connect — Solution Approach, Architecture & Build Phasing

> **Customer ask**: "We love Spaarke's capabilities but we have already made significant investment in our Document Management, Matter Management, and E-billing systems. How can we take advantage of Spaarke's capabilities but not require a full migration?"

---

## 1. Context

Spaarke's value is its **AI/analysis layer** — JPS playbooks, semantic search, classification, contract review, invoice matching, entity-scoped chat, export. That value does **not** require Spaarke to own the customer's documents or matter records. Today, however, there is no first-class way for an external system to feed data into Spaarke or receive results back — every endpoint assumes Dataverse and SharePoint Embedded are the system of record.

The exploration in Phase 1 confirmed:

- **Capabilities are mature** — 25 AI endpoint groups, configurable playbooks via `GenericAnalysisHandler`, semantic search, record matching all production-ready ([src/server/api/Sprk.Bff.Api/Services/Ai/](src/server/api/Sprk.Bff.Api/Services/Ai/)).
- **Scaffolding exists** — Service Bus job processor, named API key auth, HMAC webhook validation, alternate keys for cross-environment IDs ([Services/Jobs/ServiceBusJobProcessor.cs](src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs), [Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs](src/server/api/Sprk.Bff.Api/Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs), [Api/Filters/WebhookSignatureFilter.cs](src/server/api/Sprk.Bff.Api/Api/Filters/WebhookSignatureFilter.cs)).
- **No integration framework exists** — no connectors, no MCP server, no published SDK/OpenAPI, no outbound webhooks, no "external system" seam in [docs/standards/INTEGRATION-CONTRACTS.md](docs/standards/INTEGRATION-CONTRACTS.md).
- **The BFF is already AI-dominant** — 69% of `Services/` LOC; CLAUDE.md §10 requires explicit placement justification for new modules ([.claude/constraints/bff-extensions.md](.claude/constraints/bff-extensions.md)).

**Intended outcome**: a new module — **Spaarke Connect** — that lets a customer keep their existing DMS / MMS / e-billing investments and consume Spaarke's AI capabilities via configuration, not migration. The same module is also the **migration on-ramp**: references to external documents can be promoted to managed Spaarke records as the customer chooses to consolidate.

---

## 2. Strategy & Solution Approach

### 2.1 Three commitments shape every decision below

1. **Reference, don't replicate.** External documents stay in iManage / NetDocuments / Aderant / etc. Spaarke holds a *stub* (`sprk_externalref`) with metadata and a deep link. Binaries are fetched on demand for AI analysis, then discarded (or cached transiently). Spaarke is never a second copy of the customer's DMS.
2. **Configuration over code.** A customer team or consultant — not Spaarke engineering — sets up a connection. Onboarding is an admin UX flow in a Code Page, backed by Dataverse config records, with declarative field mappings and Key Vault for secrets. Adding a new customer never requires a code deploy.
3. **Migration on-ramp, not a dead end.** The data model and APIs are designed so an `sprk_externalref` document can later be *promoted* to a managed `sprk_document` (binary moved into SPE, ref retired). Customers who eventually want full migration follow a built-in path, not a re-implementation.

### 2.2 Two integration patterns ship in parallel

Both share the same canonical contract — the prebuilt connector is just the BYO contract with the vendor-specific glue prewritten.

**Pattern A — Prebuilt Connector** (configuration only). For systems with strong demand and a stable API, Spaarke ships a vendor-specific connector. The customer/consultant clicks "Connect to iManage Work," runs OAuth, picks the libraries/workspaces to expose, and analyses start flowing.

**Pattern B — BYO Integration Gateway** (REST + webhook contract). For systems Spaarke hasn't built a prebuilt connector for yet, the customer's IT team (or a consultant) builds a thin adapter against a published, OpenAPI-documented contract. The adapter can be PowerShell, an Azure Function, a Logic App, or anything that can POST JSON and verify HMAC signatures.

> **Why both from day one**: prebuilt connectors deliver "easy" for the systems we've built; the BYO gateway means a customer with a niche system is never blocked. Both flow through the same job pipeline and data model.

### 2.3 Customer deployment context

Spaarke is deployed into the customer's Azure tenant (per the existing BFF + Dataverse model). Their legacy systems are almost always third-party SaaS with HTTPS APIs (iManage Cloud, NetDocuments, Aderant Cloud, Elite/3E Cloud, Litify on Salesforce). This means:

- **No on-prem agent is required for v1.** Spaarke Connect runs in the customer's tenant; outbound HTTPS calls to the SaaS APIs are all that's needed.
- **OAuth2 is the dominant auth pattern** (every modern legal-tech SaaS supports it). Spaarke acts as a registered application in the customer's vendor account; the customer's admin grants consent during setup.
- **Credentials and refresh tokens live in the customer's Key Vault**, never in Dataverse plaintext.
- **Network egress** uses the existing BFF App Service / Container App configuration; no new network topology.

### 2.4 Spaarke Connect as a migration on-ramp

The strategic angle: every reference is a *latent* migration. A customer who starts with read-only references can later choose, per matter or per document, to:

1. **Promote** — copy binary into SPE, update the stub to `sprk_storagemode=Internal`, retire the external reference (or keep it as bidirectional sync). This is a single admin action per record, or a bulk operation per matter.
2. **Mirror** — keep both copies in sync (changes propagated both directions). Used during a transition window.
3. **Retire external** — once promotion is verified, archive the external original.

The same data model that enables "use without migration" enables "migrate when ready." This is not extra engineering — it's a consequence of the reference model. We document the promotion path in v1 and ship the bulk-promotion tool in a later phase.

---

## 3. Architecture

### 3.1 Topology

```
                  Customer's Azure Tenant
   ┌──────────────────────────────────────────────────────────┐
   │                                                          │
   │    ┌─────────────────┐         ┌────────────────────┐    │
   │    │  Sprk.Bff.Api   │ ◄────── │  Sprk.Connect.Api  │    │
   │    │  (existing)     │  HTTPS  │  (NEW App Service) │    │
   │    │  AI capabilities│ ApiKey  │  - REST/Webhook    │    │
   │    │  + Dataverse    │         │  - OpenAPI         │    │
   │    │  + SPE          │         │  - Connector host  │    │
   │    └────────┬────────┘         └─────────┬──────────┘    │
   │             │                            │               │
   │             │                  ┌─────────▼──────────┐    │
   │             │                  │ Sprk.Connect.Worker│    │
   │             │                  │ (NEW Container App │    │
   │             │                  │  or Functions)     │    │
   │             │                  │ Service Bus consumer    │
   │             │                  │ Sync, writeback, retry  │
   │             │                  └─────────┬──────────┘    │
   │             │                            │               │
   │   ┌─────────▼──────────┐      ┌──────────▼──────────┐    │
   │   │   Dataverse        │      │   Service Bus       │    │
   │   │   (existing) + new │      │   (existing) + new  │    │
   │   │   Connect entities │      │   connect-* topics  │    │
   │   └────────────────────┘      └─────────────────────┘    │
   │                                                          │
   │             ┌─────────────────────────┐                  │
   │             │  Azure Key Vault        │                  │
   │             │  Per-tenant connection  │                  │
   │             │  secrets (OAuth tokens, │                  │
   │             │  HMAC keys, API keys)   │                  │
   │             └─────────────────────────┘                  │
   └──────────────────────────────┬───────────────────────────┘
                                  │
                              HTTPS (outbound)
                                  │
   ┌──────────────────────────────▼───────────────────────────┐
   │  Customer's Legacy SaaS Systems                          │
   │  iManage Cloud · NetDocuments · Aderant · Elite/3E · ... │
   └──────────────────────────────────────────────────────────┘
```

### 3.2 Component map and responsibilities

| Component | Type | Responsibility |
|---|---|---|
| **Sprk.Connect.Api** | New App Service | Public REST + webhook surface. Hosts prebuilt connectors as plugins. Authenticates external callers (per-tenant API key, HMAC webhooks). Enqueues work to Service Bus. Calls back into BFF for AI capabilities via internal API key. |
| **Sprk.Connect.Worker** | New Container App / Functions | Service Bus consumer. Runs sync windows, writeback to external systems, retry/DLQ. Talks to vendor APIs. Stateless. |
| **Sprk.Connect.Connectors.*** | Class libraries | One per prebuilt vendor connector. Implement `IExternalSystemConnector`. Loaded by both `Sprk.Connect.Api` (for webhook receive) and `Sprk.Connect.Worker` (for sync/push). |
| **Spaarke.Connect.Sdk** | NuGet package (future) | Partner-facing abstractions. Not shipped in MVP — keep the interfaces internal until they're stable. |
| **Connect Admin Code Page** | New React/Vite SPA | Customer/consultant UX: create connections, run OAuth, configure mappings, enable capabilities, view event log, trigger migration promotion. |
| **New Dataverse entities** | Solution components | `sprk_externalconnection`, `sprk_externalref`, `sprk_externalfieldmapping`, `sprk_externalevent` (+ alt keys on `sprk_document`). Tenant config + audit trail. |
| **Sprk.Bff.Api** (existing) | Existing | Tiny additions only: register one new internal API key scheme so Connect can call back; tag a small set of AI endpoints as `[InternalApi]`. Publish-size impact ≈ zero. |

### 3.3 Placement justification (per CLAUDE.md §10 BFF Hygiene)

Spaarke Connect lives **outside** `Sprk.Bff.Api`. Four of five decision criteria in [.claude/constraints/bff-extensions.md](.claude/constraints/bff-extensions.md) push outside the BFF:

| Criterion | Connect | Decision |
|---|---|---|
| Latency budget vs BFF session state (<500ms)? | No — async, webhook-driven | → elsewhere |
| Writes to BFF session/audit/safety in same request lifecycle? | No | → elsewhere |
| Retroactive annotation of streaming response? | No | → elsewhere |
| Event-driven, no synchronous user wait? | Yes | → separate deployable per ADR-001 |
| Thin facade exposing BFF capabilities to EXTERNAL consumers? | Yes (explicit Connect exception clause) | → elsewhere + new ADR |

This also keeps vendor SDKs (iManage REST client, future SOAP fallbacks for Elite, etc.) out of the BFF publish bundle.

### 3.4 Data flow — happy path

**Inbound (external doc → Spaarke analysis):**

1. Customer's user files a contract in iManage.
2. iManage webhook → `POST /api/v1/connect/webhooks/inbound/{connectionId}` on `Sprk.Connect.Api`.
3. `WebhookSignatureFilter` validates HMAC (reuses [Api/Filters/WebhookSignatureFilter.cs](src/server/api/Sprk.Bff.Api/Api/Filters/WebhookSignatureFilter.cs) pattern).
4. Connect upserts `sprk_externalref` (idempotent by `connectionId + externalKey + etag`), enqueues `ConnectAnalyze` job to Service Bus.
5. `Sprk.Connect.Worker` consumes the job, calls connector's `ResolveBinaryAsync` to fetch the document content from iManage, POSTs to BFF `/api/ai/analysis/execute` with the internal API key.
6. BFF runs the configured playbook (no Connect-aware code in BFF — just receives a payload like any other analysis).
7. On completion, worker writes results back: PATCHes iManage custom fields, adds a "Spaarke Analysis" version comment with the deep link to the Spaarke Analysis Workspace.
8. Optional outbound webhook fires to any subscribers (`spaarke.analysis.completed` event).

**Outbound (Spaarke result → external):** worker's writeback path is the same regardless of trigger source.

**BYO path:** identical flow, except the customer's adapter (Function/Logic App) plays the role of the prebuilt connector. It POSTs to `/api/v1/connect/documents/ingest` with the canonical envelope; from step 4 onward everything is the same.

### 3.5 Canonical contract (one schema for both patterns)

Inbound minimum payload:

```json
{
  "tenantId": "{guid}",
  "connectionId": "{guid}",
  "capability": "classify | contract-review | invoice-match | semantic-search | custom-playbook",
  "externalRefs": {
    "matter":   { "externalKey": "12345",  "externalUri": "imanage://..." },
    "document": { "externalKey": "DOC-99", "externalUri": "imanage://..." }
  },
  "payload": { "playbookCode": "PB-013", "...": "..." },
  "callback": { "url": "https://customer/webhook", "signingKeyRef": "..." },
  "idempotencyKey": "{guid}"
}
```

Outbound event envelope (CloudEvents 1.0 compatible — preserves Event Grid path later):

```json
{
  "eventId": "{guid}",
  "eventType": "spaarke.analysis.completed",
  "specVersion": "1.0",
  "source": "spaarke.connect",
  "subject": "{tenantId}/{externalRefId}",
  "time": "2026-05-22T...Z",
  "tenantId": "...",
  "correlationId": "...",
  "data": { /* event-specific payload, signed via HMAC */ }
}
```

### 3.6 Identity model (MVP)

Each connection authenticates as a **single service account** in the external system (OAuth2 client credentials or authorization-code-with-refresh). Audit attribution on `sprk_analysis` records the Spaarke initiator; the external user is captured as a *log field* from the webhook payload, not as a security principal.

JIT mapping of external users → Dataverse contacts is a Phase 2 add-on (small entity, no breaking changes). SCIM federation to AAD is explicitly deferred — high cost, low payoff for the analysis use case.

### 3.7 Reference data and field mapping

External systems have their own controlled vocabularies (document classes, matter types, statuses). The mapping problem is universal. The approach:

- **Defaults shipped with each prebuilt connector** — `mapping.default.json` covers ~80% of fields out of the box (e.g., iManage `DocType=CONTRACT` → `sprk_documenttype=Contract`).
- **Customer override via admin UX** — the Connect Admin Code Page renders a side-by-side mapper. Mappings stored as JSON DSL bodies in `sprk_externalfieldmapping`. No code deploy required.
- **Composable transforms** — small library of built-ins (`trim`, `truncate`, `lookupContactByEmail`, `optionSetLookup(table, dictionary)`). No arbitrary code execution in mappings.

The DSL is intentionally small in MVP — just enough to express the common cases. A more elaborate visual mapper, expression sandbox, or AI-assisted mapping suggestion can come in Phase 3.

### 3.8 What stays inside Sprk.Bff.Api (deliberately minimal)

- Register one new auth scheme `IntegrationsServiceApiKey` (named-scheme pattern already used by `BuilderAdminApiKey`, `RagApiKey` — pattern in [Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs](src/server/api/Sprk.Bff.Api/Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs)).
- Tag the small set of AI endpoints Connect needs (`/api/ai/analysis/execute`, `/api/ai/semantic-search`, `/api/ai/record-matching/*`) with an `[InternalApi]` policy that accepts the new scheme.
- Extend `JobContract.JobType` enum to include `ConnectAnalyze`, `ConnectMatch`, `ConnectWriteback` so the existing Service Bus pipeline can route them (or — preferred — keep Connect's queues separate so BFF and Connect never share queues).

No new endpoints, no new services, no new packages inside BFF.

---

## 4. Component Requirements (per component)

### 4.1 Sprk.Connect.Api

- ASP.NET Core Minimal API, .NET 8 (same stack as BFF).
- Auth: per-tenant API key (`X-Spaarke-Tenant-Key`) + HMAC for inbound webhooks (`X-Spaarke-Signature-256`).
- All inbound endpoints emit ProblemDetails (ADR-019), return `202 Accepted` with polling URL for async ops.
- OpenAPI 3.1 published at `/openapi.json` — this is the contract for BYO integrators.
- Hosts prebuilt connectors as in-process plugins loaded from a curated allow-list (no reflection from arbitrary paths).
- Calls BFF via `HttpClient("BffInternal")` with `IntegrationsServiceApiKey`.
- App Service or Container App, same SKU pattern as BFF.

### 4.2 Sprk.Connect.Worker

- Background service consuming `connect-jobs` Service Bus topic (separate from existing `sdap-jobs` to avoid coupling).
- One handler per `JobType` (`ConnectAnalyze`, `ConnectMatch`, `ConnectIngest`, `ConnectWriteback`, `ConnectSync`).
- Idempotency: every job carries `IdempotencyKey`; handler short-circuits if `sprk_externalevent` already has a `Completed` row for that key.
- Retry/DLQ: 5 attempts, exponential backoff (1s, 2s, 4s, 8s, 16s), DLQ writes a `sprk_externalevent` row with severity `Error`.
- Stateless — scale out by adding replicas.

### 4.3 Sprk.Connect.Connectors.iManageWork10 (pilot prebuilt connector)

- Implements `IExternalSystemConnector` + `IDocumentConnector`.
- OAuth2 authorization-code with refresh token; refresh ahead of expiry in a hosted service.
- Webhook subscription management (create/renew per connection).
- Custom field writeback (`Spaarke_Classification`, `Spaarke_RiskFlags`, `Spaarke_AnalysisId`, `Spaarke_AnalysisUrl`).
- Document binary fetch via iManage REST.
- Defaults: `connector.manifest.json` + `mapping.default.json`.

### 4.4 New Dataverse entities

| Entity | Fields (core) | Purpose |
|---|---|---|
| `sprk_externalconnection` | type, displayName, baseUrl, status, secretRefs (KV), enabledCapabilities, defaultPlaybook | One row per (tenant, connector instance) |
| `sprk_externalref` | connectionId, externalSystemKey, externalEtag, displayName, size, mimeType, url, status, lastSyncedAt, analysisId | Document/record stub |
| `sprk_externalfieldmapping` | connectionId, sourceEntity, targetEntity, rules (JSON) | Declarative mapping DSL bodies |
| `sprk_externalevent` | connectionId, eventType, severity, idempotencyKey, payload, error, occurredAt | Append-only audit log |

Plus a new alt key on `sprk_document`: `(sprk_externalsystem, sprk_externalkey)` for cross-environment stability, and `sprk_storagemode` choice (`Internal` / `External`) to discriminate stubs from managed docs.

### 4.5 Connect Admin Code Page

- React/Vite SPA in `src/solutions/ConnectorAdmin/`, Fluent v9.
- Auth: reuses `@spaarke/auth` (true SSO requirement per [feedback_auth-true-sso-requirement.md](C:\Users\RalphSchroeder\.claude\projects\c--code-files-spaarke\memory\feedback_auth-true-sso-requirement.md)).
- Five primary screens:
  1. **Connections** — list, create, OAuth consent kickoff, status indicator.
  2. **Field Mappings** — side-by-side mapper with default-vs-override view.
  3. **Capabilities** — which AI capabilities are enabled per connection; which playbook each calls.
  4. **Event Log** — filterable view of `sprk_externalevent` with retry/DLQ replay.
  5. **Migration** *(Phase 3)* — promote refs to managed records (single or bulk).
- Customer team / consultant is the persona — no Spaarke engineering required to onboard a tenant.

### 4.6 Security & secret handling

- All OAuth tokens, HMAC signing keys, and API keys live in **Azure Key Vault**, never in Dataverse plaintext.
- Dataverse `sprk_externalconnection` stores only Key Vault *references* (same pattern as `BFF-API-ClientSecret`).
- Managed Identity on `Sprk.Connect.Api` and `.Worker` is granted Key Vault `secrets/get` only — no list, no set.
- Per-tenant access policies in hosted SaaS (Model 1); per-tenant Vault in customer-hosted SaaS (Model 2). Mirrors existing auth-deployment-setup.md pattern.
- API keys hashed at rest for comparison ([Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs](src/server/api/Sprk.Bff.Api/Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs) uses constant-time comparison already).

### 4.7 Observability

- Application Insights namespace `Spaarke.Connect.*` with per-connector telemetry namespaces (`Spaarke.Connect.iManageWork10`).
- `sprk_externalevent` is the customer-visible audit trail; App Insights is the engineering audit trail.
- Every job logs `correlationId`, `tenantId`, `connectionId`, `idempotencyKey`.
- Health endpoint `/healthz` on each connector reports OAuth credential validity + endpoint reachability.

---

## 5. High-level Packaging & Build Phasing

> **Phasing strategy**: ship value early in **Phase 1 (MVP)**; **every architectural decision in Phase 1 preserves the path to Phase 2 and 3 without rebuild**. The connector framework, mapping DSL, and event schema are designed up front; what we *defer* is the partner program scaffolding (NuGet SDK release, manifest signing, certification process), additional connectors, and MCP.

### Phase 1 — MVP (≈12 weeks, ships to first design-partner customer)

**Goal**: One real customer running real contract analyses against documents that live in iManage, with results written back to iManage custom fields.

**Scope**:
- `Sprk.Connect.Api` + `Sprk.Connect.Worker` deployed.
- One prebuilt connector: **iManage Work 10**.
- BYO REST + webhook gateway (same surface; just lacks a vendor adapter).
- New Dataverse entities (`sprk_externalconnection`, `sprk_externalref`, `sprk_externalfieldmapping`, `sprk_externalevent`, alt keys on `sprk_document`).
- Connect Admin Code Page — Connections + Field Mappings + Capabilities + Event Log screens.
- Four exposed capabilities:
  1. **Document classification + entity extraction** (winner — solves iManage's known weak spot).
  2. **Contract Review playbook** (universal legal-AI table stakes).
  3. **Semantic search across linked external docs** (returns iManage URLs, not Spaarke URLs).
  4. **Invoice → Matter matching** (for e-billing customers; uses existing `/api/ai/record-matching/*`).
- Two new ADRs: **ADR-029 Spaarke Connect Architecture** and **ADR-030 Reference-not-Replicate Document Strategy**.
- `INTEGRATION-CONTRACTS.md` updated with **Seam 10 (BFF → Connect)** and **Seam 11 (Connect → External Systems)**.

**Explicitly out of Phase 1**: NuGet SDK release, partner certification, MCP server, additional connectors, bulk historical sync, per-user identity mapping, migration promotion UX, declarative-mapping visual designer beyond a basic side-by-side mapper, on-prem agent.

**Customer onboarding (Phase 1)** — what a consultant does:
1. Spin up Connect in the customer's tenant (Bicep + the existing azure-deploy skill pattern; ~30 minutes).
2. In Connect Admin: create an `sprk_externalconnection` for iManage. Click "Authenticate" → OAuth consent in iManage.
3. Use the default field mapping or override per-field. Save.
4. Enable Classification + Contract Review + Semantic Search capabilities; pick playbooks.
5. Smoke-test by filing a contract in iManage; observe end-to-end flow.

Customer time to first analysis: **target under 60 minutes**.

### Phase 2 — Connector Library + Platform Hardening (≈3 months after Phase 1 GA)

**Goal**: Reduce per-customer onboarding cost to near-zero for the most common legacy stacks. Harden the runtime for multi-tenant scale.

**Scope**:
- Three additional prebuilt connectors, prioritized by demand × engineering cost:
  - **SharePoint Online (Graph)** — lowest engineering cost; reuses existing Graph stack; serves small/mid firms using SP as DMS.
  - **NetDocuments** — second-largest legal DMS.
  - **Aderant Expert** — covers Matter Management *and* E-billing in one connector.
- JIT external user → Dataverse contact mapping (`sprk_externaluser` entity, optional opt-in).
- Outbound event bus formalization (CloudEvents 1.0, subscriptions via `sprk_eventsubscription` entity).
- Connector health dashboard in Admin Code Page.
- Bulk historical ingestion via paginated `Fetch` API (for customers who want to backfill).
- Hardening: circuit breakers, OAuth refresh-failure handling, DLQ replay UX, per-tenant rate limiting.

### Phase 3 — Migration On-Ramp + Partner SDK (≈6 months after Phase 1 GA)

**Goal**: Spaarke Connect becomes the bridge by which customers consolidate onto Spaarke at their own pace; partners ship their own connectors.

**Scope**:
- **Migration tooling**: per-record and per-matter promotion (`sprk_externalref` → managed `sprk_document` with binary in SPE). UI in Connect Admin Code Page.
- **Mirror mode**: bidirectional sync window for customers in transition (configurable per connection).
- **Spaarke.Connect.Sdk NuGet release** — partner-facing abstractions stabilized, manifest signing, certification flow.
- **Spaarke.Connect.TestKit NuGet** — in-memory fake capability surface for partner development.
- **Two more connectors** by customer demand (likely Elite/3E and Litify).
- **Optional MCP facade** (`Sprk.Connect.Mcp`) if customer demand exists — exposes a curated subset of capabilities as MCP tools for Claude Desktop / Copilot Studio agents.

### Phase 4 — Long-tail and Ecosystem (after Phase 3)

- Visual mapping designer with AI-assisted field suggestions.
- Marketplace UX (browse/install connectors from a registry).
- On-prem agent for customers with legacy on-prem systems.
- Per-user identity federation (SCIM provisioning) for customers requiring true row-level security carry-over.

---

## 6. Critical Files

**Existing files referenced as patterns / minimal changes**:

- [src/server/api/Sprk.Bff.Api/Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs](src/server/api/Sprk.Bff.Api/Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs) — pattern for the new per-tenant + internal-service API key schemes.
- [src/server/api/Sprk.Bff.Api/Api/Filters/WebhookSignatureFilter.cs](src/server/api/Sprk.Bff.Api/Api/Filters/WebhookSignatureFilter.cs) — reused verbatim for inbound HMAC validation.
- [src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs](src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs) — pattern for `Sprk.Connect.Worker`; ADR-004 `JobContract` is reused.
- [src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs](src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs) — the config-driven AI executor Connect invokes (no changes; just consumed).
- [.claude/constraints/bff-extensions.md](.claude/constraints/bff-extensions.md) — binding constraint cited in ADR-029 placement justification.
- [docs/standards/INTEGRATION-CONTRACTS.md](docs/standards/INTEGRATION-CONTRACTS.md) — add Seams 10 and 11 here.
- [docs/data-model/entity-relationship-model.md](docs/data-model/entity-relationship-model.md) — update with the new Connect entities.

**New projects to create** (paths illustrative):

- `src/server/connect/Sprk.Connect.Api/` — App Service / Container App.
- `src/server/connect/Sprk.Connect.Worker/` — background worker.
- `src/server/connect/Sprk.Connect.Connectors.Abstractions/` — interfaces (kept internal in Phase 1).
- `src/server/connect/Sprk.Connect.Connectors.iManageWork10/` — pilot prebuilt connector.
- `src/server/connect/Sprk.Connect.Transform/` — mapping DSL runtime.
- `src/solutions/ConnectorAdmin/` — admin Code Page (React/Vite).

**New ADRs**:

- `.claude/adr/ADR-029-spaarke-connect-architecture.md`
- `.claude/adr/ADR-030-reference-not-replicate-documents.md`

**New Dataverse solution components**:

- Entities: `sprk_externalconnection`, `sprk_externalref`, `sprk_externalfieldmapping`, `sprk_externalevent`.
- Field additions to `sprk_document`: `sprk_externalkey`, `sprk_externalsystem`, `sprk_storagemode`.
- Alt key on `sprk_document`: `(sprk_externalsystem, sprk_externalkey)`.

---

## 7. Verification Plan

End-to-end proof points that gate Phase 1 GA:

1. **Tenant provisioning** — Bicep deploys `Sprk.Connect.Api` + `.Worker` into a clean Azure tenant in < 30 minutes; smoke-test endpoints reachable.
2. **Connection onboarding** — A consultant (or any admin with the right Dataverse role) completes the OAuth consent flow against an iManage Cloud trial tenant via the Admin Code Page; `sprk_externalconnection.status = Active` within 60 seconds.
3. **Webhook receive** — Postman-simulated iManage webhook posts to `/api/v1/connect/webhooks/inbound/{connectionId}` and produces a row in `sprk_externalevent` with `status = Received`. Invalid HMAC is rejected with `401`.
4. **End-to-end analysis with writeback** — A contract filed in iManage is classified + reviewed by Spaarke within 90 seconds; results appear in iManage custom fields + a version comment with the Analysis Workspace deep link.
5. **BYO path** — A simulated PowerShell adapter POSTs an invoice payload to `/api/v1/connect/documents/ingest`; record matching returns the correct `sprk_matter` with confidence score.
6. **Mapping override** — Customer changes the document-type mapping for "ENG_LETTER" → `Engagement Letter` in the Admin Code Page; the next webhook reflects the new mapping without any code deploy.
7. **Failure & retry** — Worker survives a 30-second iManage outage; the job retries with exponential backoff and eventually succeeds; no duplicate `sprk_analysis` is created.
8. **Idempotency** — Replaying the same webhook payload twice produces exactly one `sprk_analysis` (verified via `idempotencyKey` in `sprk_externalevent`).
9. **Security audit** — No OAuth tokens, signing keys, or API keys appear in logs ([.claude/constraints/ai.md](.claude/constraints/ai.md) rule 15 audit); Managed Identity is the only path to Key Vault.
10. **Publish-size impact on BFF** — `dotnet publish` of `Sprk.Bff.Api` shows ≤ +0.5 MB delta from the baseline pre-Connect; no new transitive HIGH-severity CVEs ([.claude/constraints/azure-deployment.md](.claude/constraints/azure-deployment.md) baseline).

Pilot acceptance: a real design-partner customer with an existing iManage tenant signs up, completes OAuth, files a contract in iManage, sees the analysis writeback in iManage's UI within 90 seconds, and follows the deep-link to the Spaarke Analysis Workspace for human review — all without Spaarke engineering touching their environment.
