# Sprint 2 Architecture Updates

**Date:** 2025-09-30
**Status:** Post-Sprint 2 Complete
**Author:** Spaarke Engineering

---

## Executive Summary

Sprint 2 successfully delivered all planned features and revealed several architectural insights that inform Sprint 3+ planning. This document captures architecture updates, new ADRs, and refinements to the Spaarke Technical Architecture based on Sprint 2 learnings.

**Key Updates:**
- ✅ New ADR-011: Dataset PCF Controls Over Subgrids
- ✅ Validated thin plugin pattern (ADR-002)
- ✅ Confirmed PCF preference over JavaScript web resources (ADR-006)
- ✅ Established CORS and authentication patterns for Power Platform integration
- ✅ Proven asynchronous processing architecture with Service Bus

---

## New Architecture Decision Records

### ADR-011: Dataset PCF Controls Over Native Subgrids ✨ NEW

**Location:** [`docs/adr/ADR-011-dataset-pcf-over-subgrids.md`](../adr/ADR-011-dataset-pcf-over-subgrids.md)

**Status:** Accepted
**Date:** 2025-09-30

**Key Decision:**
Build custom Dataset PCF controls instead of using native Power Platform subgrids for list-based document management scenarios.

**Rationale:**
- Sprint 2 revealed limitations of JavaScript web resources for complex UI
- Native subgrids lack customization, reusability, and advanced interactions
- Dataset PCF controls provide:
  - Reusable components across forms, dashboards, custom pages
  - Advanced interactions (drag-drop, bulk operations, inline editing)
  - Modern development practices (TypeScript, React, unit testing)
  - Performance optimization (virtual scrolling, caching)

**Impact on Sprint 3:**
- Task 3.2 changed from "JavaScript to PCF migration" to "Dataset PCF Control implementation"
- Estimated effort: 24-32 hours
- Deliverables: Reusable Dataset PCF control with virtual scrolling, CRUD operations, and configuration props

**When to Use:**
- ✅ Related document lists on entity forms
- ✅ Document search/browse interfaces
- ✅ Bulk operations and selection
- ✅ Advanced filtering and sorting
- ✅ Custom visualizations (cards, tiles)

**When NOT to Use:**
- ❌ Simple read-only reference lists
- ❌ Admin configuration lists
- ❌ Scenarios with < 20 records and no custom actions

---

## Validated Architecture Decisions

### ADR-002: No Heavy Plugins (Thin Plugin Pattern) ✅ VALIDATED

**Sprint 2 Evidence:**
- Document Event Plugin executes in < 50ms (target: < 200ms)
- Successfully queues events to Service Bus without blocking user operations
- Clean separation of concerns: Plugin captures events, Background Service processes logic
- Zero synchronous file operations in plugin (all delegated to async processing)

**Confirmation:**
The thin plugin pattern works excellently. Continue this pattern for all future Dataverse plugins.

**Sprint 3 Implications:**
- Task 3.1 (Container Creation Plugin) should follow same thin pattern
- Plugin: Create SPE container via BFF API, update record, < 200ms execution
- Heavy operations (permissions, lifecycle management) delegated to async processing

---

### ADR-006: Prefer PCF Over Web Resources ✅ VALIDATED with CAVEATS

**Sprint 2 Evidence:**
- JavaScript web resource (`DocumentOperations.js`) successfully implemented with ~1000 lines
- **Limitations discovered:**
  - Namespace isolation required workarounds (explicit window attachment)
  - CORS configuration complexity
  - Limited UI capabilities (no drag-drop, progress indicators, etc.)
  - Poor reusability (separate code for each scenario)
  - Difficult testing (no unit tests, manual browser testing only)

**Confirmation:**
PCF preference is correct. Sprint 2 JavaScript was acceptable for rapid prototyping but should be replaced with PCF in Sprint 3.

**Sprint 3 Plan:**
- Migrate Sprint 2 JavaScript to Field PCF Control (single file management)
- Create Dataset PCF Control (list-based scenarios) per ADR-011
- Retire JavaScript web resource after PCF migration complete

---

### ADR-004: Async Job Contract ✅ VALIDATED

**Sprint 2 Evidence:**
- Background Service (DocumentEventProcessor) successfully processes events from Service Bus
- Idempotency tracking prevents duplicate processing
- Retry policies handle transient failures gracefully
- Dead-letter queue captures unprocessable messages for investigation

**Confirmation:**
Asynchronous processing architecture is solid. Continue this pattern.

**Sprint 3 Enhancements:**
- Consider adding telemetry dashboard for job monitoring
- Implement alerting for dead-letter queue threshold
- Add job status tracking in Dataverse for user visibility

---

## Architecture Patterns Established in Sprint 2

### 1. Power Platform JavaScript Integration Pattern

**Challenge:** JavaScript web resources run in isolated iframe context, namespace not globally accessible.

**Solution Established:**
```javascript
// Explicit window attachment for global access
if (typeof window !== 'undefined') {
    window.Spaarke = window.Spaarke || {};
    window.Spaarke.Documents = window.Spaarke.Documents || {};
}

// Propagate to parent/top windows for console access
if (window.parent && window.parent !== window) {
    window.parent.Spaarke = window.Spaarke;
}
if (window.top && window.top !== window) {
    window.top.Spaarke = window.Spaarke;
}
```

**Status:** **TEMPORARY** - Works for Sprint 2, but migrate to PCF in Sprint 3 per ADR-006 and ADR-011.

---

### 2. CORS Configuration for Power Platform Integration

**Challenge:** Power Platform (HTTPS) calling BFF API requires specific CORS configuration.

**Solution Established:**
```csharp
// Program.cs - CORS with credentials support
builder.Services.AddCors(o =>
{
    o.AddPolicy("spa", p =>
    {
        if (!string.IsNullOrWhiteSpace(allowed))
        {
            p.WithOrigins(allowed.Split(',', StringSplitOptions.RemoveEmptyEntries))
             .AllowCredentials(); // Required for credentials: 'include'
        }
        p.AllowAnyHeader().AllowAnyMethod();
        p.WithExposedHeaders("request-id", "client-request-id", "traceparent");
    });
});
```

**Status:** **PRODUCTION READY** - Works for JavaScript and will work for PCF controls.

**Sprint 3 Note:** Ensure PCF controls use `credentials: 'include'` for Dataverse authentication passthrough.

---

### 3. Dataverse Web API Integration Pattern

**Challenge:** Need to query Dataverse from JavaScript/PCF without server round-trip.

**Solution Established:**
```javascript
// Dataverse Web API query from client-side
const response = await fetch(
    `/api/data/v9.2/sprk_containers(${containerId})?$select=sprk_specontainerid`,
    {
        method: 'GET',
        headers: {
            'Accept': 'application/json',
            'OData-MaxVersion': '4.0',
            'OData-Version': '4.0'
        },
        credentials: 'include' // Use user's Dataverse authentication
    }
);
```

**Status:** **PRODUCTION READY** - Use this pattern for PCF controls to fetch related data.

**Best Practices:**
- Use `$select` to fetch only needed fields (performance)
- Use `$expand` for related entities (reduce round-trips)
- Handle 401/403 errors gracefully (user lacks permissions)

---

### 4. SPE Container ID Handling

**Challenge:** SharePoint Embedded uses Graph API ID format (`b!...`), but Dataverse stores GUID.

**Lessons Learned:**
- Always store Graph API ID (the `b!...` format) in `sprk_specontainerid` field
- Never store the container identifier GUID
- When creating containers, use `createdContainer.Id` (Graph API format), not `ContainerId` property

**Sprint 2 Issue:**
Manual container creation led to incorrect ID format in Dataverse records.

**Sprint 3 Solution:**
- Task 3.1: Create Container Creation Plugin that:
  1. Triggers on `sprk_container` PreCreate
  2. Calls BFF API to create SPE container
  3. Extracts `Id` from Graph API response (correct `b!...` format)
  4. Updates `sprk_specontainerid` before save

**Status:** **CRITICAL FIX FOR SPRINT 3** - Without this, file operations fail.

---

## Architecture Component Updates

### Backend API (Spe.Bff.Api)

**Sprint 2 Additions:**
```
src/api/Spe.Bff.Api/
├── Api/
│   ├── DocumentsEndpoints.cs          # UPDATED - CORS, container drive lookup
│   └── DataverseDocumentsEndpoints.cs # NEW - Dataverse integration endpoints
├── Services/Jobs/
│   ├── DocumentEventProcessor.cs      # NEW - Background service
│   ├── DocumentEventHandler.cs        # NEW - Event handling logic
│   ├── IdempotencyService.cs          # NEW - Duplicate prevention
│   ├── DocumentEvent.cs               # NEW - Event model
│   └── DocumentEventTelemetry.cs      # NEW - Telemetry helpers
└── Program.cs                         # UPDATED - CORS credentials, HTTPS
```

**Architecture Impact:**
- ✅ Asynchronous processing pipeline validated
- ✅ Idempotency tracking prevents duplicates
- ✅ Telemetry integration provides observability
- ✅ CORS configuration supports Power Platform

---

### Dataverse Integration (Spaarke.Dataverse)

**Sprint 2 Additions:**
```
src/shared/Spaarke.Dataverse/
├── DataverseService.cs           # EXISTING - ServiceClient wrapper
├── DataverseWebApiClient.cs      # NEW - Web API HTTP client
└── DataverseWebApiService.cs     # NEW - Web API service layer
```

**Architecture Impact:**
- ✅ Web API approach avoids .NET 8 compatibility issues
- ✅ HTTP client approach provides better performance
- ✅ Separate from ServiceClient for flexibility

**Recommendation:** **Prefer Web API** for new features (performance, simplicity). Keep ServiceClient for legacy operations requiring it.

---

### Power Platform Components

**Sprint 2 Additions:**
```
power-platform/
├── plugins/
│   └── Spaarke.Plugins/
│       ├── DocumentEventPlugin.cs    # NEW - Thin plugin
│       └── Models/DocumentEvent.cs   # NEW - Event model
└── webresources/
    └── scripts/
        └── DocumentOperations.js     # NEW - File management (~1000 lines)
```

**Architecture Impact:**
- ✅ Thin plugin pattern validated (< 50ms execution)
- ⚠️ JavaScript web resource is TEMPORARY (migrate to PCF in Sprint 3)

**Sprint 3 Plan:**
- Replace `DocumentOperations.js` with Field PCF Control
- Add Dataset PCF Control for list scenarios (ADR-011)
- Create Container Creation Plugin (thin pattern)

---

## Technical Debt & Improvements

### Identified Technical Debt from Sprint 2

1. **JavaScript Web Resource (HIGH PRIORITY)**
   - **Debt:** ~1000 lines of JavaScript with no unit tests, limited reusability
   - **Sprint 3 Resolution:** Migrate to Field PCF Control (Task 3.2)
   - **Effort:** 16-20 hours of 24-32 hour PCF task

2. **SPE Container ID Manual Entry (CRITICAL)**
   - **Debt:** Users must manually enter SPE Container ID in correct format
   - **Sprint 3 Resolution:** Container Creation Plugin (Task 3.1)
   - **Effort:** 8-12 hours

3. **Development Environment URL Hardcoding (MEDIUM)**
   - **Debt:** JavaScript/PCF points to localhost:7073 for DEV
   - **Sprint 3 Resolution:** Azure Deployment + Environment Config (Task 3.3)
   - **Effort:** 4-6 hours of 16-24 hour deployment task

4. **No Automated Plugin Registration (LOW)**
   - **Debt:** Manual plugin registration via PAC CLI
   - **Future Resolution:** CI/CD pipeline for plugin deployment
   - **Effort:** 4-6 hours (Sprint 4+)

---

## Sprint 3 Architecture Implications

### Updated Sprint 3 Task Breakdown

Based on Sprint 2 learnings and new ADRs:

#### Task 3.1: Container Creation Plugin (8-12h) - CRITICAL
**Architecture Impact:** Fixes SPE Container ID issue
**Pattern:** Thin plugin (ADR-002)
**Dependencies:** Sprint 2 BFF API

**Deliverables:**
- PreCreate plugin on `sprk_container`
- Calls BFF `/api/containers` endpoint
- Updates `sprk_specontainerid` with Graph API format
- < 200ms execution time

#### Task 3.2: PCF File Management Controls (24-32h) - HIGH PRIORITY
**Architecture Impact:** Implements ADR-006 and ADR-011
**Pattern:** Field PCF + Dataset PCF
**Dependencies:** Sprint 2 BFF API, Task 3.1

**Deliverables:**
- **Field PCF Control:** Single file management (replaces Sprint 2 JavaScript)
  - Upload, download, replace, delete operations
  - Progress indicators, error handling
  - Reusable across forms and custom pages

- **Dataset PCF Control:** List-based scenarios (NEW per ADR-011)
  - Virtual scrolling for performance
  - Bulk operations and selection
  - Configurable for different entities and view modes

#### Task 3.3: Azure Deployment & DevOps (16-24h) - HIGH PRIORITY
**Architecture Impact:** Production infrastructure
**Pattern:** Multi-environment deployment
**Dependencies:** Sprint 2 complete

**Deliverables:**
- Azure App Service deployment (DEV/UAT/PROD)
- Key Vault integration for secrets
- CI/CD pipeline for API and PCF
- Environment-specific configuration

---

## Architecture Diagram Updates

### Current Architecture (Sprint 2 Complete)

```
┌─────────────────────┐     ┌──────────────────────┐     ┌─────────────────┐
│  Power Platform     │────▶│   BFF API (.NET 8)   │────▶│   Dataverse     │
│                     │     │                      │     │                 │
│ - Model-Driven App  │     │ - REST Endpoints     │     │ - sprk_document │
│ - JavaScript WebRes │     │ - CORS + Credentials │     │ - sprk_container│
│ - Thin Plugin       │     │ - Graph SDK v5       │     │ - Security      │
│                     │     │ - Dataverse Web API  │     └─────────────────┘
└─────────────────────┘     └──────────────────────┘
                                     │
                                     ▼
                            ┌──────────────────────┐     ┌─────────────────┐
                            │  SharePoint Embedded │────▶│   Azure Graph   │
                            │                      │     │                 │
                            │ - File Storage       │     │ - Container API │
                            │ - Container Types    │     │ - Drive API     │
                            │ - Drive Management   │     │ - Item API      │
                            └──────────────────────┘     └─────────────────┘
                                     │
                                     ▼
                            ┌──────────────────────┐     ┌─────────────────┐
                            │  Azure Service Bus   │────▶│ Background Svc  │
                            │                      │     │                 │
                            │ - Event Queue        │     │ - Event Process │
                            │ - Retry Policies     │     │ - Idempotency   │
                            │ - Dead Letter Queue  │     │ - Telemetry     │
                            └──────────────────────┘     └─────────────────┘
```

### Planned Architecture (Sprint 3+)

```
┌─────────────────────┐     ┌──────────────────────┐     ┌─────────────────┐
│  Power Platform     │────▶│   BFF API (.NET 8)   │────▶│   Dataverse     │
│                     │     │                      │     │                 │
│ - Model-Driven App  │     │ - REST Endpoints     │     │ - sprk_document │
│ - Field PCF Control │     │ - CORS + Credentials │     │ - sprk_container│
│ - Dataset PCF       │     │ - Graph SDK v5       │     │ - Security      │
│ - Thin Plugins (2)  │     │ - Dataverse Web API  │     └─────────────────┘
│   • DocumentEvent   │     │ - Azure Deployment   │
│   • ContainerCreate │     └──────────────────────┘
└─────────────────────┘              │      │
                                     │      └──────▶ Azure Key Vault (Secrets)
                                     ▼
                            ┌──────────────────────┐     ┌─────────────────┐
                            │  SharePoint Embedded │────▶│   Azure Graph   │
                            │                      │     │                 │
                            │ - File Storage       │     │ - Container API │
                            │ - Container Types    │     │ - Drive API     │
                            │ - Auto-provisioned   │     │ - Item API      │
                            └──────────────────────┘     └─────────────────┘
                                     │
                                     ▼
                            ┌──────────────────────┐     ┌─────────────────┐
                            │  Azure Service Bus   │────▶│ Background Svc  │
                            │                      │     │                 │
                            │ - Event Queue        │     │ - Event Process │
                            │ - Retry Policies     │     │ - Idempotency   │
                            │ - Dead Letter Queue  │     │ - Telemetry     │
                            └──────────────────────┘     └─────────────────┘
```

**Key Changes:**
- ✅ JavaScript replaced with PCF controls
- ✅ Container Creation Plugin added
- ✅ Azure deployment with Key Vault
- ✅ Auto-provisioned SPE containers

---

## Success Metrics - Sprint 2 Validation

| Metric | Target | Sprint 2 Actual | Status |
|--------|--------|-----------------|--------|
| **Plugin Performance** | < 200ms | ~50ms avg | ✅ Exceeded |
| **API Response Time** | < 3s | < 2s | ✅ Exceeded |
| **File Operations** | 4/4 working | 4/4 working | ✅ Met |
| **Code Coverage** | > 60% | ~40% | ⚠️ Below target |
| **Zero Prod Errors** | 0 critical | 0 (not in prod) | ✅ Met |

**Areas for Improvement:**
- Unit test coverage (target: 80% for Sprint 3)
- Integration test suite (add in Sprint 3)

---

## Recommendations for Sprint 3+

### Immediate (Sprint 3)

1. **✅ Implement ADR-011** - Dataset PCF Control
2. **✅ Migrate JavaScript to PCF** - Field PCF Control
3. **✅ Fix SPE Container ID** - Container Creation Plugin
4. **✅ Deploy to Azure** - Production infrastructure

### Near-Term (Sprint 4)

1. **Enhance Test Coverage**
   - Unit tests: 80%+ coverage
   - Integration tests for PCF controls
   - E2E tests for critical flows

2. **Monitoring & Observability**
   - Application Insights dashboards
   - Alerting for Service Bus dead-letter threshold
   - Performance monitoring for PCF controls

3. **Performance Optimization**
   - Implement caching strategy (ADR-009: Redis First)
   - Virtual scrolling for large datasets
   - Lazy loading of file metadata

### Future (Sprint 5+)

1. **Advanced Features**
   - Document versioning
   - Bulk operations
   - Advanced search and filtering
   - AI-powered document insights

2. **DevOps Maturity**
   - Automated PCF deployment pipeline
   - Blue-green deployments
   - Feature flags for gradual rollout

---

## Conclusion

Sprint 2 successfully delivered all planned features and validated core architectural decisions:
- ✅ Thin plugin pattern works excellently
- ✅ Asynchronous processing with Service Bus is solid
- ✅ BFF API architecture is production-ready
- ✅ PCF preference over JavaScript confirmed

**New ADR (ADR-011)** establishes clear direction for Dataset PCF controls over native subgrids.

**Sprint 3 Focus:**
- Fix SPE Container ID automation (Plugin)
- Migrate to PCF controls (Field + Dataset)
- Deploy to Azure (Production infrastructure)

**Architecture is sound and ready for Sprint 3!** 🚀

---

## References

- [Sprint 2 Wrap-Up Report](../../dev/projects/sdap_project/Sprint 2/SPRINT-2-WRAP-UP-REPORT.md)
- [ADR-011: Dataset PCF Over Subgrids](../adr/ADR-011-dataset-pcf-over-subgrids.md)
- [ADR-006: Prefer PCF Over Web Resources](../adr/ADR-006-prefer-pcf-over-webresources.md)
- [ADR-002: No Heavy Plugins](../adr/ADR-002-no-heavy-plugins.md)
- [Technical Architecture v2](../specs/SPRK_Technical Architecture_v2.docx)

---

**Document Status:** Final
**Last Updated:** 2025-09-30
**Next Review:** Sprint 3 Retrospective
