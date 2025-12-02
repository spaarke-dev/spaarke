# ADR: No Outbound HTTP from Dataverse Plugins

**Status**: Accepted
**Date**: 2025-11-22
**Context**: SPE File Viewer Implementation

---

## Decision

**Dataverse plugins MUST NOT make outbound HTTP calls to external services.**

Plugins are transaction-scoped guardrails only. All external I/O (Graph API, SPE, OAuth, web services) belongs in `Spe.Bff.Api` endpoints or `BackgroundService` workers.

---

## Context

During implementation of the SPE File Viewer feature, an initial approach was proposed using:
- Custom API (`sprk_GetFilePreviewUrl`)
- Plugin (`GetFilePreviewUrlPlugin`) that makes HTTP calls to the BFF API
- BFF API calls SharePoint Embedded

This violated SDAP architecture principles.

---

## Rationale

### Why No HTTP from Plugins

1. **Transaction Scope Violation**
   - Plugins run within Dataverse transactions
   - Network I/O introduces latency and unpredictability
   - Transient failures (timeouts, 503s) roll back the entire transaction
   - Not friendly to plugin sandbox timeout limits (2 minutes default)

2. **Complexity and Maintenance Burden**
   - OAuth token management in sandboxed environment
   - Retry/backoff logic duplicated
   - Dependency management (Azure.Identity, HttpClient, etc.)
   - ILMerge/ILRepack complications (no longer best practice)

3. **Security and Auditability**
   - Plugins run with elevated privileges (SYSTEM context)
   - Mixing user-driven operations with service-principal calls is confusing
   - Harder to audit external calls when scattered across plugins
   - Client secrets in Dataverse config tables (sprk_externalserviceconfig) expose secrets

4. **Missing Capabilities**
   - CAE (Continuous Access Evaluation) handling
   - Regional endpoint awareness
   - Clock-skew handling
   - Token caching and refresh
   - Polly resilience policies
   - Structured logging and correlation IDs

5. **Architectural Misalignment**
   - SDAP architecture: BFF centralizes external I/O
   - Plugins should be thin validators/projections
   - External operations belong in BackgroundService workers triggered by webhooks/queues

---

## Correct Patterns

### ✅ For Client-Initiated Operations (Read/Query)

**PCF Control → BFF API → External Service**

```
PCF (React/TypeScript)
  ↓ MSAL.js acquires BFF token
  ↓ Scope: api://<BFF_APP_ID>/SDAP.Access
  ↓
BFF Endpoint (GET /api/documents/{id}/preview-url)
  ↓ DocumentAuthorizationFilter (UAC)
  ↓ App-only token (service principal)
  ↓
SharePoint Embedded (Graph API)
```

**When to Use**:
- User-driven read operations
- Real-time responses needed (preview URLs, file metadata)
- No Dataverse transaction involved

**Example**: SPE File Viewer preview URL retrieval

### ✅ For Server-Initiated Operations (Async/Background)

**Plugin → Service Bus → BackgroundService → External Service**

```
Plugin (PreValidate/PostOperation)
  ↓ Validates inputs
  ↓ Enqueues message to Service Bus
  ↓ Transaction completes quickly
  ↓
BackgroundService (in Spe.Bff.Api)
  ↓ Dequeues message
  ↓ Calls external services with retry/backoff
  ↓ Updates Dataverse on completion
```

**When to Use**:
- Long-running operations (OCR, virus scan, content extraction)
- Operations that can be async
- External service calls that might be slow/unreliable

**Example**: Document OCR processing after upload

### ✅ For Transaction-Scoped Operations (Validation/Projection)

**Plugin → Dataverse Operations Only**

```
Plugin (PreValidate/PreOperation)
  ↓ Validates inputs against business rules
  ↓ Queries Dataverse for related records
  ↓ Throws InvalidPluginExecutionException if invalid
  ↓ (No external HTTP calls)
```

**When to Use**:
- Data validation before save
- Calculated fields from Dataverse data
- Enforcing referential integrity

**Example**: Validate matter access before allowing document create

---

## Alternatives Considered

### Alternative 1: Custom API with Plugin HTTP Calls (Rejected)

**Pros**:
- Server-side execution (no Graph token in browser)
- Centralized entry point via Custom API

**Cons**:
- ❌ Violates "no HTTP from plugins" rule
- ❌ Dependency management nightmare (Azure.Identity, etc.)
- ❌ Plugin timeout risks
- ❌ Secrets in Dataverse config tables
- ❌ No retry/resilience policies
- ❌ Harder to audit and debug

**Decision**: Rejected

### Alternative 2: PCF → BFF Direct (Accepted)

**Pros**:
- ✅ Aligns with SDAP architecture
- ✅ BFF centralizes auth, retry, audit
- ✅ Standard OAuth flow (MSAL.js)
- ✅ No plugin dependencies
- ✅ Easier to test and maintain

**Cons**:
- Requires MSAL.js in PCF
- PCF needs BFF token configuration

**Decision**: Accepted

### Alternative 3: Plugin → Queue → Worker (Valid for Async)

**Pros**:
- ✅ Plugin stays transaction-scoped
- ✅ Worker handles I/O with proper retry
- ✅ Aligns with architecture

**Cons**:
- Not suitable for synchronous operations (preview URLs need immediate response)

**Decision**: Valid for async operations, but not for this use case

---

## Consequences

### Positive

1. **Plugins remain simple and fast**
   - Transaction-scoped only
   - No network I/O latency
   - Predictable performance

2. **BFF centralizes external I/O**
   - Consistent retry/backoff policies
   - Structured logging with correlation IDs
   - Easier to audit and monitor
   - Security best practices (CAE, token caching, etc.)

3. **Easier to maintain and test**
   - No plugin dependency management
   - BFF endpoints are standard HTTP APIs (easy to test with Postman/curl)
   - PCF can mock BFF calls for local development

4. **Better security posture**
   - No secrets in Dataverse config tables
   - BFF enforces UAC via endpoint filters
   - Standard OAuth flows

### Negative

1. **PCF controls need MSAL.js**
   - Adds complexity to PCF development
   - Requires app registration setup

2. **Cannot use Custom APIs for synchronous external operations**
   - Custom APIs are bound to Dataverse entities
   - Useful for server-side logic, but not for external HTTP calls

### Mitigations

1. **Provide PCF templates and samples** with MSAL.js pre-configured
2. **Document BFF endpoint patterns** so developers know when to use BFF vs plugins
3. **Update ADR index** with clear "no HTTP from plugins" rule

---

## Implementation

### What Was Removed

- ❌ Custom API: `sprk_GetFilePreviewUrl`
- ❌ Plugin Assembly: `Spaarke.Dataverse.CustomApiProxy`
- ❌ Plugin Type: `GetFilePreviewUrlPlugin`
- ❌ External Service Config table (optional - kept for potential future use)

### What Was Added

- ✅ PCF Control: `FileViewer` with MSAL.js
- ✅ BFF Endpoint: `GET /api/documents/{id}/preview-url`
- ✅ DocumentAuthorizationFilter with real UAC
- ✅ Named MSAL scope: `api://<BFF_APP_ID>/SDAP.Access`
- ✅ Correlation ID propagation

---

## References

- [SDAP Architecture Overview](../architecture/SDAP-OVERVIEW.md)
- [BFF Pattern](../architecture/BFF-PATTERN.md)
- [Plugin Best Practices](../dataverse/PLUGIN-BEST-PRACTICES.md)
- [Implementation Plan](./IMPLEMENTATION-SDAP-ALIGNED.md)

---

## Sign-Off

**Architect**: [Your Name]
**Date**: 2025-11-22
**Reviewed By**: [Team]

**Rule**: **No outbound HTTP from Dataverse plugins. Period.**
