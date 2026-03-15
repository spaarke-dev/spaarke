# SPE Multi-Tenant Architecture & Graph Auth Framework

## Document Information

| Field | Value |
|-------|-------|
| **Author** | Ralph Schroeder |
| **Date** | 2026-03-11 |
| **Version** | 1.0 |
| **Status** | Draft |
| **Project** | spe-multi-tenant-architecture-r1 |

---

## 1. Executive Summary

Spaarke's SharePoint Embedded (SPE) integration currently operates in a single-tenant model — all SPE operations authenticate against one Entra ID tenant using a single app registration with hardcoded credentials. This works for development but cannot support production scenarios where:

1. **Spaarke hosts documents for multiple customers** in its own tenant (each customer gets isolated containers)
2. **Customers host documents in their own tenant** (Spaarke's BFF API must authenticate cross-tenant)
3. **Hybrid deployments** exist where some customers use Spaarke-hosted SPE and others bring their own tenant

This project delivers a multi-tenant Graph authentication framework, automated SPE container provisioning, and the customer configuration model required to support all three hosting scenarios. It is foundational infrastructure for production deployment — no customer onboarding can occur without this work.

---

## 2. Problem Statement

### Current Architecture (Single-Tenant)

```
BFF API → GraphClientFactory (single TENANT_ID) → Graph API → Spaarke's SPE containers
```

**Hardcoded at startup** (from `GraphClientFactory.cs`):
```csharp
var tenantId = configuration["TENANT_ID"];  // Set once, used for all requests
builder.WithAuthority($"https://login.microsoftonline.com/{tenantId}");
```

**Single container type** (from `appsettings.json`):
```json
{
  "DEFAULT_CT_ID": "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c"
}
```

### What Breaks in Production

| Scenario | What Fails | Why |
|----------|-----------|-----|
| Customer A documents in Spaarke tenant, Customer B documents in their own tenant | OBO token exchange uses wrong authority | `TENANT_ID` is hardcoded to Spaarke's tenant |
| Two customers in same Spaarke tenant | Container listing returns all containers | No per-customer filtering on `ListContainersAsync` |
| Customer revokes Graph consent | All SPE operations fail for all customers | Single `GraphServiceClient` shared across tenants |
| Customer-hosted SPE with different container type | Wrong container type ID used | `DEFAULT_CT_ID` is a single value |

### What Already Works (Reusable)

The codebase has partial multi-tenant support that this project extends:

- **`TenantAuthorizationFilter`** — Extracts `tid` claim from user tokens, validates tenant access (used in RAG endpoints)
- **`AnalysisOptions`** — Three deployment models (shared, dedicated, customer-owned) for AI features
- **`GraphTokenCache`** — Redis-backed OBO token cache with SHA256 hashing (needs tenant-scoped keys)
- **`SpeFileStore` facade** — Clean abstraction that delegates to operation classes (per ADR-007). Multi-tenant changes happen in `GraphClientFactory`, not in `SpeFileStore`

---

## 3. Scope

### In Scope

#### Domain A: Multi-Tenant Graph Authentication
- Refactor `GraphClientFactory` to resolve tenant dynamically per-request
- Support both app-only and OBO (On-Behalf-Of) flows per-tenant
- Tenant-scoped token caching in `GraphTokenCache` (Redis key: `sdap:graph:token:{tenantId}:{tokenHash}`)
- Multi-tenant MSAL confidential client application management
- Graceful handling of consent revocation and auth failures per-tenant

#### Domain B: Customer Configuration Model
- Customer tenant registry (which customers exist, which hosting model they use)
- Per-customer Graph credentials (stored in per-customer Key Vault or central Key Vault with customer-scoped secrets)
- Per-customer SPE configuration (container type ID, hosting model, tenant ID)
- Configuration hot-reload (add customer without restarting BFF API)
- Dataverse integration — customer config stored as Dataverse records or environment variables

#### Domain C: SPE Container Provisioning Automation
- Automated container creation per-customer (given a container type ID and tenant context)
- Container permission assignment (which users/groups can access)
- Container type registration documentation and tooling (customer-hosted scenario)
- Container lifecycle management (create, configure, decommission)
- Provisioning script (`Provision-SpeContainers.ps1`) for operational use

#### Domain D: Spaarke-Hosted SPE (Multi-Customer in Spaarke Tenant)
- Per-customer container isolation within Spaarke's tenant
- Container-to-customer mapping (which containers belong to which customer)
- Scoped container listing (customer sees only their containers)
- B2B guest user access to customer-specific containers
- Default hosting model for demo and standard customers

#### Domain E: Customer-Hosted SPE (Cross-Tenant)
- Multi-tenant app registration for Spaarke (published for admin consent)
- Admin consent flow documentation and verification endpoint
- Cross-tenant OBO token exchange (user in customer tenant → Graph access to customer's SPE)
- Customer IT onboarding guide (what they need to configure in their tenant)
- Consent status monitoring and health check per-customer

#### Domain F: Graph Permission Model
- Document minimum required Graph permissions for each hosting model
- `FileStorageContainer.Selected` vs broader permissions — trade-offs and requirements
- Permission request justification document (for customer IT review)
- Conditional Access compatibility (customer's CA policies must not block Spaarke)
- Service principal vs delegated permission strategy per operation type

### Out of Scope

- **Customer-hosted AI resources** — Follows same pattern but is a separate project
- **Dataverse multi-environment management** — Handled by production deployment project
- **Customer billing/subscription** — Business concern, not architectural
- **Self-service registration app** — Consumes this project's provisioning APIs, built separately
- **Data migration between hosting models** — Future project if customers want to switch
- **Power Platform licensing decisions** — Business/legal concern

### Affected Areas

| Area | Files/Components | Impact |
|------|-----------------|--------|
| Graph Auth | `Infrastructure/Graph/GraphClientFactory.cs` | Major refactor — dynamic tenant resolution |
| Token Cache | `Infrastructure/Graph/GraphTokenCache.cs` | Moderate — tenant-scoped cache keys |
| Configuration | `Configuration/` (new) | New — `CustomerTenantOptions`, `SpeHostingOptions` |
| DI Registration | `Infrastructure/DI/DocumentsModule.cs` | Moderate — conditional registration |
| SpeFileStore | `Infrastructure/Graph/SpeFileStore.cs` | Minor — pass tenant context through |
| Container Ops | `Infrastructure/Graph/ContainerOperations.cs` | Moderate — tenant-aware container CRUD |
| Endpoints | `Api/Endpoints/ContainerEndpoints.cs` | Moderate — customer context in requests |
| Filters | `Api/Filters/TenantAuthorizationFilter.cs` | Extend — SPE tenant validation |
| Scripts | `scripts/` (new) | New — provisioning scripts |
| Infrastructure | `infrastructure/bicep/` | Minor — Key Vault secret structure |

---

## 4. Requirements

### Functional Requirements

1. **FR-01: Dynamic Tenant Resolution** — BFF API MUST resolve the target SPE tenant per-request based on user context (Entra ID `tid` claim) and customer configuration, not from a static config value.

2. **FR-02: Dual Hosting Model** — System MUST support both Spaarke-hosted SPE (containers in Spaarke's tenant) and customer-hosted SPE (containers in customer's tenant) simultaneously, with per-customer configuration determining which model applies.

3. **FR-03: Customer Tenant Registry** — System MUST maintain a registry of customer tenants with their SPE hosting configuration, Graph credentials, container type IDs, and consent status. Registry MUST be queryable at runtime without app restart.

4. **FR-04: Spaarke-Hosted Container Isolation** — When multiple customers use Spaarke-hosted SPE, each customer's containers MUST be logically isolated. `ListContainersAsync` MUST return only containers belonging to the requesting customer.

5. **FR-05: Cross-Tenant OBO** — For customer-hosted SPE, BFF API MUST perform OBO token exchange against the customer's tenant authority (`https://login.microsoftonline.com/{customerTenantId}`) using customer-specific app credentials.

6. **FR-06: Container Provisioning API** — System MUST provide an API endpoint and/or script to create SPE containers for a customer, assign permissions, and register the container in the customer configuration.

7. **FR-07: Admin Consent Verification** — For customer-hosted SPE, system MUST verify that the customer has granted admin consent to Spaarke's multi-tenant app before attempting Graph operations.

8. **FR-08: Tenant-Scoped Token Cache** — `GraphTokenCache` MUST include tenant ID in cache keys to prevent cross-tenant token leakage. Key format: `sdap:graph:token:{tenantId}:{tokenHash}`.

9. **FR-09: Credential Isolation** — Each customer's Graph credentials (client secret or certificate) MUST be stored separately, either in per-customer Key Vault or in customer-scoped secrets within the platform Key Vault.

10. **FR-10: Container Type Registration Guide** — For customer-hosted SPE, system MUST provide clear documentation for customer IT to register the SPE Container Type in their tenant and grant required permissions.

11. **FR-11: Health Check Per-Tenant** — System MUST expose a health check endpoint that validates Graph connectivity and consent status per customer tenant (used for monitoring and onboarding verification).

12. **FR-12: Graceful Degradation** — If a single customer's Graph auth fails (consent revoked, credentials expired), only that customer's SPE operations MUST fail. All other customers MUST continue operating normally.

### Non-Functional Requirements

- **NFR-01: Auth Latency** — OBO token exchange MUST remain under 300ms p95 (current: ~200ms uncached, ~5ms cached). Multi-tenant resolution MUST NOT add more than 10ms overhead.
- **NFR-02: Cache Hit Rate** — Maintain >95% token cache hit rate (current: 97%) with tenant-scoped keys.
- **NFR-03: Credential Security** — Customer Graph credentials MUST be stored in Azure Key Vault (not app settings). Key Vault references MUST be used in App Service configuration.
- **NFR-04: Zero-Downtime Onboarding** — Adding a new customer tenant MUST NOT require BFF API restart or deployment.
- **NFR-05: Audit Trail** — All cross-tenant Graph operations MUST be logged with customer tenant ID for audit purposes.
- **NFR-06: Backward Compatibility** — Existing single-tenant behavior MUST continue working during migration. Default tenant falls back to `TENANT_ID` config if no customer-specific config exists.

---

## 5. Technical Approach

### 5.1 Graph Auth Provider Architecture

Replace the current single-tenant `GraphClientFactory` with a tenant-aware provider:

```
Current:
  GraphClientFactory (singleton, one tenant)
    └── ForApp() → GraphServiceClient
    └── ForUserAsync(ctx) → GraphServiceClient

Proposed:
  ITenantGraphClientProvider (scoped, resolved per-request)
    ├── ResolveAsync(customerId) → TenantGraphContext
    │   ├── Spaarke-hosted → SpaarkeGraphContext (Spaarke's credentials)
    │   └── Customer-hosted → CustomerGraphContext (customer's credentials)
    ├── ForAppAsync(customerId) → GraphServiceClient
    └── ForUserAsync(ctx, customerId) → GraphServiceClient
```

**Key design decisions:**

1. **`SpeFileStore` interface does NOT change** — Per ADR-007, the facade exposes SDAP types only. The tenant context flows through as an additional parameter or ambient context (e.g., `HttpContext` already carries `tid` claim).

2. **MSAL Confidential Client per tenant** — Each tenant gets its own `IConfidentialClientApplication` instance (cached, not created per-request). MSAL handles token lifecycle internally.

3. **Customer ID resolution** — Extract from Dataverse user context (the BFF already knows which customer the user belongs to from auth context). Map to tenant configuration.

### 5.2 Customer Configuration Model

```json
{
  "customers": {
    "demo": {
      "displayName": "Spaarke Demo",
      "speHosting": "spaarke-tenant",
      "tenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
      "containerTypeId": "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
      "graphAuth": {
        "method": "platform-credentials",
        "keyVaultSecretName": null
      },
      "containers": ["container-guid-1", "container-guid-2"]
    },
    "contoso": {
      "displayName": "Contoso Legal",
      "speHosting": "customer-tenant",
      "tenantId": "b332b06f-7def-5545-bfdd-f59449b2c3g3",
      "containerTypeId": "9b7df45d-7166-5792-9g98-3c5a0f032d17",
      "graphAuth": {
        "method": "client-credentials",
        "keyVaultSecretName": "graph-contoso-client-secret",
        "clientId": "multi-tenant-app-guid"
      },
      "containers": [],
      "consentStatus": "granted",
      "consentVerifiedUtc": "2026-03-10T14:30:00Z"
    }
  }
}
```

**Storage options** (to be decided during implementation):
- **Option A**: Dataverse custom table (`sprk_customertenant`) — queryable, UI-editable, follows existing patterns
- **Option B**: JSON in Key Vault — simple, secure, but harder to query
- **Option C**: BFF API configuration (appsettings) — requires restart for changes
- **Recommendation**: Option A (Dataverse) for registry, Key Vault for credentials

### 5.3 SPE Hosting Models

#### Model A: Spaarke-Hosted (Default)

```
User (B2B guest in Spaarke tenant)
  → BFF API (Spaarke tenant)
    → Graph API (Spaarke tenant)
      → SPE Container (Spaarke tenant, customer-assigned)
```

- Spaarke's app registration has Graph permissions in Spaarke's tenant
- Containers created in Spaarke's tenant, tagged/mapped to customer
- B2B guest users access via OBO (user's token → Spaarke Graph token)
- Container isolation via customer-to-container mapping (not Graph-level isolation)
- **Customer IT effort**: Zero (Spaarke manages everything)

#### Model B: Customer-Hosted

```
User (native in customer tenant, OR B2B guest)
  → BFF API (Spaarke tenant)
    → Graph API (customer tenant, via multi-tenant app consent)
      → SPE Container (customer tenant)
```

- Spaarke publishes a **multi-tenant app registration**
- Customer admin grants consent via `https://login.microsoftonline.com/{customerTenant}/adminconsent?client_id={spaarkeAppId}`
- BFF API uses customer-specific authority for OBO exchange
- SPE Container Type registered in customer's tenant (customer IT action)
- **Customer IT effort**: Moderate (admin consent + container type registration + optional Conditional Access config)

### 5.4 Container Provisioning Flow

#### Spaarke-Hosted Container Provisioning

```
1. Admin calls: POST /api/admin/customers/{customerId}/containers
   Body: { "displayName": "Contoso Documents", "description": "..." }

2. BFF resolves customer config → speHosting = "spaarke-tenant"

3. BFF uses Spaarke's app-only GraphClient:
   POST /storage/fileStorage/containers
   { "containerTypeId": "{spaarke-ct-id}", "displayName": "..." }

4. Graph returns container ID

5. BFF registers container in customer config:
   - Updates customer registry (Dataverse record)
   - Assigns container permissions (if needed)

6. Returns container details to admin
```

#### Customer-Hosted Container Provisioning

```
1. PREREQUISITE: Customer IT has:
   a. Registered SPE Container Type in their tenant
   b. Granted admin consent to Spaarke's multi-tenant app
   c. Provided: tenantId, containerTypeId to Spaarke

2. Spaarke admin configures customer in registry:
   - tenantId, containerTypeId, graphAuth credentials

3. BFF verifies consent: GET /api/admin/customers/{customerId}/consent-status
   → Calls Graph to verify app has access to customer tenant

4. Admin calls: POST /api/admin/customers/{customerId}/containers
   Body: { "displayName": "Contoso Documents" }

5. BFF resolves customer config → speHosting = "customer-tenant"

6. BFF uses customer-tenant GraphClient (client credentials):
   POST /storage/fileStorage/containers
   { "containerTypeId": "{customer-ct-id}", "displayName": "..." }

7. Graph creates container in customer's tenant

8. BFF registers container in customer config

9. Returns container details
```

### 5.5 Graph Permissions Strategy

#### Spaarke-Hosted: Single-Tenant App Registration

| Permission | Type | Justification |
|-----------|------|---------------|
| `FileStorageContainer.Selected` | Application | Create/manage specific SPE containers |
| `Files.ReadWrite.All` | Delegated | User-context file operations (OBO) |
| `User.Read` | Delegated | Identify calling user |

#### Customer-Hosted: Multi-Tenant App Registration

| Permission | Type | Justification | Customer IT Concern |
|-----------|------|---------------|---------------------|
| `FileStorageContainer.Selected` | Application | Manage containers Spaarke created | Low — scoped to specific containers |
| `Files.ReadWrite.All` | Delegated | User file operations in their tenant | Medium — broad but user-scoped |
| `User.Read` | Delegated | Identify user | Low |

**Permission minimization strategy**:
- Use `.Selected` permissions wherever possible (scoped to specific resources)
- Avoid `Sites.FullControl.All` in customer tenants (too broad)
- Document exactly which operations need which permissions
- Provide customer IT with a permission justification document

### 5.6 Security Considerations

| Concern | Mitigation |
|---------|------------|
| Cross-tenant token leakage | Tenant ID in cache keys, tenant validation on every Graph call |
| Credential compromise (one customer) | Per-customer credentials in Key Vault, isolated secret rotation |
| Consent revocation detection | Health check endpoint, Graph error handling detects 403/consent errors |
| B2B guest privilege escalation | `TenantAuthorizationFilter` validates user's `tid` matches target tenant |
| Admin endpoint abuse | Admin endpoints require Spaarke admin role, not customer user |
| Token replay | MSAL handles token lifetime, Redis cache TTL < token expiry |

---

## 6. Phased Implementation

### Phase 1: Foundation (Multi-Tenant Graph Auth)
- Refactor `GraphClientFactory` → `TenantGraphClientProvider`
- Tenant-scoped token cache keys
- Customer configuration model (Dataverse table + Key Vault)
- Backward compatibility (single-tenant mode as default)
- Unit tests for tenant resolution

### Phase 2: Spaarke-Hosted Multi-Customer
- Per-customer container mapping
- Scoped container listing (filter by customer)
- Container provisioning API (admin endpoint)
- B2B guest access validation
- Integration tests

### Phase 3: Customer-Hosted SPE (Cross-Tenant)
- Multi-tenant app registration setup
- Admin consent flow and verification
- Cross-tenant OBO implementation
- Customer IT onboarding documentation
- Consent monitoring health check

### Phase 4: Provisioning Automation
- `Provision-SpeContainers.ps1` script
- Container lifecycle management (create, configure, decommission)
- Container type registration guide and tooling
- Permission assignment automation
- Operational runbook

### Phase 5: Production Hardening
- Credential rotation automation
- Comprehensive audit logging
- Error handling for consent revocation
- Performance testing (multi-tenant token cache under load)
- Security review and penetration test

---

## 7. Dependencies

### Prerequisites
- BFF API must be running and deployable (current state: yes)
- Azure Key Vault accessible for credential storage
- Dataverse dev environment available for customer config table
- Entra ID admin access for app registration changes

### External Dependencies
- Microsoft Graph API (SPE endpoints — currently in beta)
- Entra ID multi-tenant app registration capabilities
- Customer IT cooperation (for customer-hosted scenarios)

### Related Projects
- **production-performance-improvement-r1** — Infrastructure hardening (VNet, Key Vault) feeds into credential security
- **spaarke-self-service-registration-app** — Consumes provisioning APIs built here
- **Production environment setup** — Depends on this project for Graph auth foundation

---

## 8. Success Criteria

1. [ ] BFF API can authenticate to Graph API for multiple tenants simultaneously
2. [ ] Spaarke-hosted SPE: Customer A cannot see Customer B's containers
3. [ ] Customer-hosted SPE: BFF can create containers in a customer's tenant via admin-consented multi-tenant app
4. [ ] Adding a new customer does not require BFF API restart
5. [ ] OBO token cache hit rate remains >95% with tenant-scoped keys
6. [ ] Auth latency overhead from multi-tenant resolution is <10ms
7. [ ] Single customer's auth failure does not affect other customers
8. [ ] Customer IT onboarding documentation is complete and tested with a real external tenant
9. [ ] Container provisioning can be performed via API and/or script
10. [ ] All Graph operations are logged with customer tenant ID
11. [ ] Credentials are stored in Key Vault, not in app settings

---

## 9. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| SPE Graph APIs remain in beta, breaking changes | Medium | High | Pin Graph SDK version, abstract API calls, monitor changelog |
| `FileStorageContainer.Selected` insufficient for all operations | Medium | Medium | Test permission scope thoroughly, document fallback to broader permissions |
| Customer IT blocks admin consent (security review delays) | High | Medium | Prepare comprehensive permission justification document upfront |
| MSAL multi-tenant token caching complexity | Medium | Medium | Use MSAL's built-in distributed token cache, test thoroughly |
| Cross-tenant OBO fails with customer Conditional Access | Medium | High | Document CA compatibility requirements, test with CA-enabled tenant |

---

## 10. Open Questions

These should be resolved during the design-to-spec interview:

1. **Container-to-customer mapping storage**: Dataverse custom table vs. container custom properties (Graph supports custom metadata on containers) vs. BFF-side mapping table?

2. **Multi-tenant app registration**: Should Spaarke publish to the Azure AD app gallery (for discoverability), or use direct admin consent URLs only?

3. **Container Type registration for customer-hosted**: Can this be automated via Graph API, or does customer IT always do this manually in SharePoint admin?

4. **Credential type preference**: Client secrets (simpler, rotate every 2 years) vs. certificates (more secure, auto-rotatable via Key Vault)?

5. **Customer config hot-reload mechanism**: Poll Dataverse on interval? Use Dataverse webhooks to push changes? Use Redis-cached config with TTL?

6. **Scope of admin endpoints**: Should container provisioning be API-only (called by self-service registration app), script-only (manual operation), or both?

---

*This design document covers the full scope of SPE multi-tenant architecture. It should be transformed to spec.md via `/design-to-spec` and then initialized via `/project-pipeline`.*
