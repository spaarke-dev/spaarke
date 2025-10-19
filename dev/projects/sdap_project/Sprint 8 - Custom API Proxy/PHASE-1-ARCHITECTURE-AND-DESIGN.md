# Phase 1: Architecture and Design

**Status**: ✅ Complete
**Duration**: 0.5 days
**Prerequisites**: Sprint 7 completion, understanding of authentication issue

---

## Phase Objectives

Design and document the complete Custom API Proxy architecture including:
- Component architecture and boundaries
- Authentication flow patterns
- Data model and entity relationships
- Security model and access controls
- Deployment strategy
- Testing approach

---

## Context for AI Vibe Coding

### Problem Statement
PCF controls in Power Apps model-driven apps cannot directly call external APIs like Spe.Bff.Api because they don't have access to user Azure AD tokens. This is a security feature by design - PCF controls run in sandboxed client-side JavaScript and cannot access authentication tokens.

### Current Architecture Gap
```
User → PCF Control → ❌ BLOCKED → Spe.Bff.Api (requires JWT token)
```

### Solution Architecture
```
User → PCF Control → Dataverse Custom API (implicit auth) → Spe.Bff.Api (service-to-service auth)
```

### Key Design Principles
1. **Separation of Concerns**: Custom API Proxy is a separate, reusable infrastructure component
2. **Not Tightly Coupled**: Can proxy to ANY external API, not just Spe.Bff.Api
3. **Security First**: Leverage Dataverse implicit authentication + service-to-service patterns
4. **Production Ready**: Comprehensive error handling, monitoring, observability

---

## Tasks Completed

### ✅ Task 1.1: Create Sprint 8 Folder Structure
```bash
mkdir -p "dev/projects/sdap_project/Sprint 8 - Custom API Proxy"
```

### ✅ Task 1.2: Document Architecture Overview
Created `CUSTOM-API-PROXY-ARCHITECTURE.md` with:
- Executive summary
- Architecture diagrams (current problem vs solution)
- Component design
- Authentication flow (11-step detailed flow)
- Data model (entities and relationships)
- Implementation guidance
- Security considerations
- Deployment strategy
- Testing approach
- Extensibility patterns
- Monitoring and observability

### ✅ Task 1.3: Document Sprint Overview
Created `SPRINT-8-OVERVIEW.md` with:
- Sprint goals and objectives
- Background and problem statement
- Architecture principles
- 6-phase task breakdown
- Success criteria (must have, should have, nice to have)
- Dependencies (upstream and downstream)
- Risks and mitigations
- Timeline estimates
- Related documentation links

### ✅ Task 1.4: Create Phase-Specific Documentation
This document and subsequent phase documents for AI-directed implementation.

---

## Architecture Decisions

### Decision 1: Custom API Proxy vs MSAL.js
**Decision**: Use Custom API Proxy (Dataverse server-side APIs)
**Rationale**:
- Better security (no tokens in client-side JavaScript)
- Leverages existing Dataverse authentication
- Meets enterprise compliance requirements
- Centralized control and monitoring
- Better error handling and retry logic

**Reference**: See `Sprint 7/CUSTOM-API-VS-MSAL-ANALYSIS.md`

### Decision 2: Generic vs Operation-Specific Custom APIs
**Decision**: Implement BOTH patterns
- **Generic API**: `sprk_ProxyExecute` for flexibility
- **Operation-Specific APIs**: `sprk_ProxyDownloadFile`, `sprk_ProxyDeleteFile`, etc. for type safety

**Rationale**:
- Operation-specific APIs provide better type safety and IntelliSense
- Generic API allows quick addition of new operations without deployment
- Hybrid approach maximizes flexibility and developer experience

### Decision 3: External Service Configuration Storage
**Decision**: Store configurations in Dataverse entity `sprk_externalserviceconfig`
**Rationale**:
- No code changes needed to add/modify external services
- Leverage Dataverse security for sensitive configurations
- Easy to manage via model-driven app
- Supports multiple environments (dev, test, prod)

### Decision 4: Authentication Pattern
**Decision**: Use Azure.Identity with ClientSecretCredential for service-to-service auth
**Rationale**:
- Industry standard pattern
- Works in Dataverse plugin sandbox
- Supports managed identity for future enhancement
- Secure token acquisition and caching

### Decision 5: Audit Logging
**Decision**: Create dedicated `sprk_proxyauditlog` entity
**Rationale**:
- Compliance and security requirements
- Debugging and troubleshooting
- Performance monitoring
- Cost analysis (track API usage)

---

## Component Architecture

### 1. Dataverse Custom API Layer
- **Base Custom API**: `sprk_ProxyExecute` (generic proxy)
- **Operation-Specific APIs**:
  - `sprk_ProxyDownloadFile`
  - `sprk_ProxyDeleteFile`
  - `sprk_ProxyReplaceFile`
  - `sprk_ProxyUploadFile`

### 2. Plugin Class Hierarchy
```
IPlugin (Microsoft.Xrm.Sdk)
    ↓
BaseProxyPlugin (abstract)
    ├── Common setup and teardown
    ├── Authentication logic
    ├── Logging and telemetry
    ├── Error handling and retries
    └── Configuration retrieval
    ↓
Operation-Specific Plugins
    ├── ProxyExecutePlugin (generic)
    ├── ProxyDownloadFilePlugin
    ├── ProxyDeleteFilePlugin
    ├── ProxyReplaceFilePlugin
    └── ProxyUploadFilePlugin
```

### 3. Data Model

#### Entity: sprk_externalserviceconfig
Stores external service configurations (base URL, auth settings, timeouts, retry policies)

**Key Fields**:
- `sprk_name` (unique identifier, e.g., "SpeBffApi")
- `sprk_baseurl` (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net")
- `sprk_authtype` (OptionSet: None, ClientCredentials, ManagedIdentity, ApiKey)
- `sprk_clientid`, `sprk_clientsecret`, `sprk_scope` (for Azure AD auth)
- `sprk_timeout`, `sprk_retrycount`, `sprk_retrydelay` (operational settings)
- `sprk_isenabled`, `sprk_healthstatus` (runtime status)

#### Entity: sprk_proxyauditlog
Tracks all proxy operations for security, debugging, and compliance

**Key Fields**:
- `sprk_operation` (e.g., "DownloadFile")
- `sprk_servicename` (e.g., "SpeBffApi")
- `sprk_correlationid` (for distributed tracing)
- `sprk_requestpayload`, `sprk_responsepayload` (JSON, redacted)
- `sprk_statuscode`, `sprk_duration` (performance metrics)
- `sprk_success`, `sprk_errormessage` (outcome tracking)

### 4. Authentication Flow

**11-Step Flow**:
1. User clicks button in PCF control (e.g., "Download")
2. PCF control calls `context.webAPI.execute("sprk_ProxyDownloadFile", { ... })`
3. Dataverse validates user session (implicit authentication)
4. Custom API plugin executes with system privileges
5. Plugin retrieves service configuration from `sprk_externalserviceconfig`
6. Plugin acquires service token using ClientSecretCredential
7. Plugin calls external API (Spe.Bff.Api) with bearer token
8. External API validates token and processes request
9. Plugin processes response (convert to Base64, extract metadata)
10. Dataverse returns response to PCF control
11. PCF control processes response (decode Base64, trigger download)

**Key Security Points**:
- PCF control NEVER sees authentication tokens
- User authentication handled by Dataverse (implicit)
- Service authentication handled by plugin (ClientSecretCredential)
- Secrets stored in Dataverse secured fields or Azure Key Vault

---

## Security Model

### Entity-Level Security
- `sprk_externalserviceconfig`: System Administrator only
- `sprk_proxyauditlog`: Read-only for auditors

### Field-Level Security
- `sprk_clientsecret`: Secured field, encrypted at rest
- `sprk_apikey`: Secured field, encrypted at rest

### Custom API Security
- Custom APIs require specific privileges (e.g., `prvReadsprk_document`)
- User must have access to underlying data (e.g., document record)
- Plugin validates user permissions before proxying request

### Data Security
- Sensitive data redaction in audit logs
- Encryption at rest (Dataverse)
- Encryption in transit (HTTPS only)

---

## Deployment Strategy

### Development Environment
1. Create Dataverse solution: `SpaarkeCustomApiProxy`
2. Deploy entities and relationships
3. Build and sign plugin assembly
4. Register plugins with Plugin Registration Tool
5. Create Custom API definitions
6. Configure external service in Dataverse
7. Update PCF control
8. Test end-to-end

### Production Environment
1. Export managed solution from development
2. Import managed solution to production
3. Configure external service connections (prod URLs and secrets)
4. Test with limited users
5. Monitor for errors and performance
6. Gradual rollout

---

## Testing Approach

### Unit Tests
- Test plugin logic in isolation using fakes
- Mock external service calls
- Validate error handling and retries

### Integration Tests
- Test end-to-end with real Dataverse environment
- Validate authentication flow
- Test all file operations

### Performance Tests
- Measure latency (target: < 2 seconds per operation)
- Test throughput (concurrent operations)
- Validate retry and circuit breaker logic

---

## Extensibility Patterns

### Adding New External Services

**Steps**:
1. Create configuration record in `sprk_externalserviceconfig`
2. Create Custom API definition
3. Implement plugin inheriting from `BaseProxyPlugin`
4. Update PCF control with new API client method
5. Deploy and test

**Example**: Adding Azure Blob Storage proxy
- Service name: "AzureBlobStorage"
- Base URL: "https://mystorageaccount.blob.core.windows.net"
- Auth type: ManagedIdentity
- Custom API: `sprk_ProxyUploadBlob`

### Extensibility Points
- `BaseProxyPlugin.ValidateRequest()` - Override for custom validation
- `BaseProxyPlugin.LogRequest()` - Override for custom logging
- HTTP interceptors - Add middleware for headers, retries, etc.

---

## Monitoring and Observability

### Key Metrics
1. **Latency**: P50, P95, P99 per operation
2. **Availability**: Success rate, error rate
3. **Throughput**: Requests per second, data volume
4. **Errors**: By type, by service, by user

### Monitoring Tools
- Application Insights (telemetry from plugins)
- Dataverse audit logs
- Azure Dashboard or Power BI report

### Alerting
- Circuit breaker trips
- High error rates
- Performance degradation
- Authentication failures

---

## Success Criteria

### Must Have ✅
- Custom API Proxy architecture designed and documented
- Authentication flow validated
- Security model defined
- Data model designed
- Deployment strategy documented

### Should Have ✅
- Extensibility patterns documented
- Testing approach defined
- Monitoring strategy planned

### Nice to Have ✅
- Performance benchmarks estimated
- Cost analysis documented
- Migration path from direct API calls

---

## Deliverables

✅ `SPRINT-8-OVERVIEW.md` - Sprint overview and task breakdown
✅ `CUSTOM-API-PROXY-ARCHITECTURE.md` - Comprehensive architecture guide (25,000+ words)
✅ `PHASE-1-ARCHITECTURE-AND-DESIGN.md` - This document

---

## Next Steps

Proceed to **Phase 2: Dataverse Custom API Foundation**

**Phase 2 will**:
- Create Dataverse solution structure
- Implement base plugin class with common functionality
- Create configuration and audit entities
- Add logging and telemetry infrastructure
- Write unit tests for base functionality

**References for Phase 2**:
- `CUSTOM-API-PROXY-ARCHITECTURE.md` - Section "Implementation Guidance"
- `docs/DATAVERSE-AUTHENTICATION-GUIDE.md` - Dataverse authentication patterns
- `docs/KM-DATAVERSE-PLUGIN-DEVELOPMENT.md` - Plugin development standards (if exists)

---

## Knowledge Resources

### Internal Documentation
- [Sprint 7 Authentication Architecture Issue](../Sprint%207/AUTHENTICATION-ARCHITECTURE-ISSUE.md)
- [Custom API vs MSAL Analysis](../Sprint%207/CUSTOM-API-VS-MSAL-ANALYSIS.md)
- [Dataverse Authentication Guide](../../../docs/DATAVERSE-AUTHENTICATION-GUIDE.md)
- [Sprint 4 Completion Summary](../Sprint%204/TASK-4.4-BACKUP-COMPLETE.md)

### External Resources
- [Dataverse Custom API Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- [Plugin Development Guide](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/plug-ins)
- [Azure.Identity Documentation](https://learn.microsoft.com/en-us/dotnet/api/azure.identity)
- [PCF Framework API](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/reference/)

### Code References
- Plugin base implementation: See `CUSTOM-API-PROXY-ARCHITECTURE.md` section "Implementation Guidance"
- PCF client implementation: See `CUSTOM-API-PROXY-ARCHITECTURE.md` section "Phase 4: Update PCF Control"

---

## Notes for AI Vibe Coding

When implementing subsequent phases:

1. **Always reference this architecture** - Don't deviate without documenting the decision
2. **Follow established patterns** - Use `BaseProxyPlugin` pattern for all plugins
3. **Security first** - Never log sensitive data, always validate user access
4. **Error handling** - Comprehensive try-catch, meaningful error messages
5. **Logging** - Trace at key points for debugging
6. **Testing** - Write tests before deployment
7. **Documentation** - Update docs as implementation progresses

**Code Style**:
- C# plugins: Follow Microsoft coding conventions
- TypeScript PCF: Use async/await, proper error handling
- Comments: Explain "why", not "what"
- Naming: Clear, descriptive names (no abbreviations unless standard)

**Git Workflow**:
- Create feature branch per phase
- Commit frequently with clear messages
- Reference sprint/task in commit messages
- Update sprint documentation before marking phase complete
