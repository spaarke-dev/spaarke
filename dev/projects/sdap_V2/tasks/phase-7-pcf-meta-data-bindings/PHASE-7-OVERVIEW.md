# Phase 7: Navigation Property Metadata Service - Implementation Plan

**Status:** Ready to Start
**Priority:** HIGH
**Estimated Duration:** 3-4 days
**Dependencies:** Phase 6 Complete (v2.2.0 deployed)

---

## Executive Summary

Implement a server-side navigation property metadata service to enable **scalable multi-parent support** for the PCF document upload control. This eliminates manual PowerShell validation and positions the solution for growth to 10+ parent entities.

**Current State (Phase 6):**
- ✅ Working for Matter entity (hardcoded `sprk_Matter` navigation property)
- ❌ Manual validation required for each new parent entity (30 min/entity)
- ❌ Hardcoded values risk becoming stale if schema changes
- ❌ Only 1 of 5 configured entities validated

**Target State (Phase 7):**
- ✅ Server auto-discovers navigation properties from Dataverse metadata
- ✅ Add new parent entities in 15-30 minutes (config only, no validation)
- ✅ Future-proof against schema changes
- ✅ 3-layer resilience (server → session cache → hardcoded fallback)
- ✅ All 5+ entities supported automatically

---

## Business Decisions (Confirmed)

Based on assessment review, the following decisions have been made:

### 1. Deploy Timing: NOW ✅
- **Decision:** Proceed with Phase 7.1 implementation immediately
- **Rationale:** Low risk, high preparedness value, positions for growth
- **Protection:** BFF changes are additive (new endpoint), no breaking changes to existing upload API

### 2. Server-Side Batch: DEFER ⏸️
- **Decision:** Do NOT implement Phase 7.2 (batch creation) at this time
- **Rationale:** Current sequential creation works fine for typical use (1-5 files)
- **Note:** User mentioned batch might already exist - will verify during implementation
- **Revisit:** When bulk upload >10 files becomes common requirement

### 3. Server Cache TTL: 5 MINUTES ✅
- **Decision:** Use 5-minute TTL for server-side metadata cache
- **Rationale:** Balances freshness (metadata changes visible within 5 min) vs performance (reduces Dataverse calls)

### 4. Server Query Strategy: ALL PARENTS ✅
- **Decision:** Query metadata for all configured parents at once
- **Rationale:** Simpler implementation, enables full client-side caching, typical count is 5-10 entities
- **Alternative Considered:** Query on-demand (deferred for future optimization)

---

## Architecture Overview

### 3-Layer Metadata Resolution

```
┌─────────────────────────────────────────────────────────┐
│  PCF Control (Client)                                   │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Layer 1: Server NavMap API                       │ │
│  │ GET /api/pcf/dataverse-navmap?v=1                │ │
│  │ ├─ Success → Cache in memory + sessionStorage    │ │
│  │ └─ Failure → Try Layer 2                         │ │
│  └───────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Layer 2: Session Storage Cache                   │ │
│  │ sessionStorage.getItem('navmap::env::v1')        │ │
│  │ ├─ Hit → Use cached data                         │ │
│  │ └─ Miss → Try Layer 3                            │ │
│  └───────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Layer 3: Hardcoded Fallback (Phase 6 values)    │ │
│  │ const NAVMAP_FALLBACK = { sprk_matter: {...} }  │ │
│  │ └─ Always available, tested, working            │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│  Spe.Bff.Api (Server)                                   │
│  ┌───────────────────────────────────────────────────┐ │
│  │ NavMapController                                  │ │
│  │ [HttpGet("api/pcf/dataverse-navmap")]            │ │
│  │ ├─ INavigationMetadataService                    │ │
│  │ │  ├─ Memory Cache (5 min TTL)                   │ │
│  │ │  └─ IDataverseService (metadata queries)       │ │
│  │ └─ Returns: NavMap { parent: NavEntry }          │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│  Dataverse (Metadata)                                   │
│  EntityDefinitions → Navigation Properties              │
└─────────────────────────────────────────────────────────┘
```

---

## Data Structures

### Server Response: NavMap

```csharp
public record NavEntry(
    string EntitySet,              // "sprk_matters"
    string LookupAttribute,        // "sprk_matter"
    string NavProperty,            // "sprk_Matter" (case-sensitive!)
    string? CollectionNavProperty  // "sprk_matter_document" (for future Option B)
);

public record NavMap
{
    public Dictionary<string, NavEntry> Parents { get; init; }
    public string Version { get; init; }
    public DateTime GeneratedAt { get; init; }
}
```

### Client Cache: NavMapCache

```typescript
type NavEntry = {
  entitySet: string;              // "sprk_matters"
  lookupAttribute: string;        // "sprk_matter"
  navProperty: string;            // "sprk_Matter"
  collectionNavProperty?: string; // "sprk_matter_document"
};

type NavMap = Record<string, NavEntry>; // Key: parent entity logical name
```

---

## Task Breakdown

### Task 7.1: Extend IDataverseService with Metadata Methods
**Duration:** 4-6 hours
**Owner:** Backend Developer
**File:** `src/shared/Spaarke.Dataverse/IDataverseService.cs`

**Deliverables:**
- Add metadata query methods to IDataverseService interface
- Implement in DataverseWebApiService or DataverseServiceClientImpl
- Unit tests for metadata queries

---

### Task 7.2: Create NavMapController in Spe.Bff.Api
**Duration:** 2-4 hours
**Owner:** Backend Developer
**Files:**
- `src/api/Spe.Bff.Api/Api/NavMapController.cs`
- `src/api/Spe.Bff.Api/Services/NavigationMetadataService.cs`

**Deliverables:**
- NavMapController with GET endpoint
- NavigationMetadataService with caching
- Configuration for parent entity list
- Integration with existing auth/observability

---

### Task 7.3: Create NavMapClient in PCF
**Duration:** 4-6 hours
**Owner:** Frontend Developer
**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/NavMapClient.ts`

**Deliverables:**
- NavMapClient with 3-layer fallback
- Session storage integration
- Error handling and logging
- TypeScript compilation successful

---

### Task 7.4: Integrate NavMapClient with DocumentRecordService
**Duration:** 2-4 hours
**Owner:** Frontend Developer
**Files:**
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts`
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`

**Deliverables:**
- Load NavMap on PCF initialization
- Use NavMap in document creation
- Maintain backward compatibility
- Error handling for unsupported entities

---

### Task 7.5: Testing and Validation
**Duration:** 4-6 hours
**Owner:** QA / All Developers

**Test Scenarios:**
1. Happy path: Matter entity with server NavMap
2. Fallback: Server down, use session cache
3. Fallback: Session clear, use hardcoded
4. Multi-entity: Test Project, Invoice, Account, Contact
5. Error: Unknown parent entity, clear error message
6. Performance: Cache hit rate >95%
7. Telemetry: Events logged correctly

---

### Task 7.6: Deployment and Documentation
**Duration:** 2-4 hours
**Owner:** DevOps / Technical Writer

**Deliverables:**
- Deploy Spe.Bff.Api with NavMap endpoint (FIRST)
- Deploy PCF v2.3.0 with NavMapClient (SECOND)
- Update configuration documentation
- Create HOW-TO-ADD-SPE-PCF-TO-NEW-ENTITIES.md
- Update deployment runbook

---

## File Structure

```
📁 spaarke/
├── 📁 src/
│   ├── 📁 api/Spe.Bff.Api/
│   │   ├── 📁 Api/
│   │   │   ├── 📄 NavMapController.cs         ← NEW (Task 7.2)
│   │   │   └── 📄 OBOEndpoints.cs             (existing)
│   │   ├── 📁 Services/
│   │   │   ├── 📄 NavigationMetadataService.cs ← NEW (Task 7.2)
│   │   │   └── 📄 ...
│   │   └── 📄 Program.cs                      (update DI registration)
│   │
│   ├── 📁 shared/Spaarke.Dataverse/
│   │   ├── 📄 IDataverseService.cs            ← UPDATE (Task 7.1)
│   │   ├── 📄 DataverseWebApiService.cs       ← UPDATE (Task 7.1)
│   │   └── 📄 ...
│   │
│   └── 📁 controls/UniversalQuickCreate/UniversalQuickCreate/
│       ├── 📁 services/
│       │   ├── 📄 NavMapClient.ts             ← NEW (Task 7.3)
│       │   ├── 📄 DocumentRecordService.ts    ← UPDATE (Task 7.4)
│       │   └── 📄 ...
│       ├── 📁 config/
│       │   └── 📄 EntityDocumentConfig.ts     (no changes)
│       └── 📄 index.ts                        ← UPDATE (Task 7.4)
│
└── 📁 dev/projects/sdap_V2/
    ├── 📁 tasks/phase-7-pcf-meta-data-bindings/
    │   ├── 📄 PHASE-7-OVERVIEW.md             (this file)
    │   ├── 📄 PHASE-7-ASSESSMENT.md           (completed)
    │   ├── 📄 PCF-META-DATA-BINDING-ENHANCEMENT.md (spec)
    │   ├── 📄 TASK-7.1-EXTEND-DATAVERSE-SERVICE.md
    │   ├── 📄 TASK-7.2-CREATE-NAVMAP-CONTROLLER.md
    │   ├── 📄 TASK-7.3-CREATE-NAVMAP-CLIENT.md
    │   ├── 📄 TASK-7.4-INTEGRATE-PCF-SERVICES.md
    │   ├── 📄 TASK-7.5-TESTING-VALIDATION.md
    │   └── 📄 TASK-7.6-DEPLOYMENT.md
    │
    └── 📁 docs/
        └── 📄 HOW-TO-ADD-SPE-PCF-TO-NEW-ENTITIES.md ← NEW
```

---

## Version Management

### Current Version
- PCF: v2.2.0 (Phase 6 - hardcoded navigation properties)
- BFF: Current production version

### Target Version
- PCF: v2.3.0 (Phase 7 - dynamic navigation properties with fallback)
- BFF: Add NavMap endpoint (non-breaking addition)

### API Versioning
- Endpoint: `/api/pcf/dataverse-navmap?v=1`
- Query parameter `v=1` allows future schema changes
- Client checks version, falls back if unsupported

---

## Risk Mitigation

### Risk 1: BFF Deployment Breaks Existing Functionality
**Mitigation:**
- NavMap endpoint is NEW, doesn't modify existing upload endpoints
- All changes to Spe.Bff.Api are additive
- Existing PCF v2.2.0 continues to work (doesn't call new endpoint)
- Deploy BFF first, test upload still works, THEN deploy PCF

**Protection Strategy:**
1. Create feature branch for BFF changes
2. Run full integration tests (upload, document creation)
3. Deploy to dev environment first
4. Verify existing PCF v2.2.0 still works
5. Deploy PCF v2.3.0 second

---

### Risk 2: Server NavMap Unavailable
**Mitigation:**
- 3-layer fallback ensures resilience
- Layer 3 (hardcoded) uses Phase 6 proven values
- PCF gracefully degrades to Phase 6 behavior

**Testing:**
- Simulate server down (return 500)
- Simulate network error (timeout)
- Verify fallback to hardcoded works

---

### Risk 3: Cache Invalidation Issues
**Mitigation:**
- Server cache: 5 min TTL (short enough for changes)
- Client cache: Session only (cleared on browser close)
- Version query param forces refresh

**Monitoring:**
- Track cache hit rate (target >95%)
- Alert if server queries spike (indicates cache failure)

---

## Success Criteria

### Phase 7.1 Complete When:

1. ✅ Server NavMap endpoint deployed and accessible
2. ✅ Server returns correct navigation properties for all configured parents
3. ✅ PCF loads NavMap from server on initialization
4. ✅ PCF creates documents using server-provided navigation properties
5. ✅ 3-layer fallback tested and working
6. ✅ All 5 configured entities tested (Matter, Project, Invoice, Account, Contact)
7. ✅ Error handling tested (unknown parent, server down)
8. ✅ Cache hit rate >95%
9. ✅ Telemetry events logging correctly
10. ✅ Documentation complete (HOW-TO guide)

---

## Timeline

### Week 1 - Backend Implementation (2 days)
- Day 1: Task 7.1 - Extend IDataverseService
- Day 2: Task 7.2 - Create NavMapController

### Week 2 - Frontend Implementation (2 days)
- Day 3: Task 7.3 - Create NavMapClient
- Day 4: Task 7.4 - Integrate with DocumentRecordService

### Week 3 - Testing and Deployment (1 day)
- Day 5 AM: Task 7.5 - Testing and Validation
- Day 5 PM: Task 7.6 - Deployment and Documentation

**Total:** 3-5 days (including buffer for issues)

---

## Rollback Plan

### If Phase 7 Deployment Fails:

**Step 1:** Keep PCF v2.2.0 deployed (don't upgrade to v2.3.0)
- Phase 6 continues to work with hardcoded values
- No impact to users

**Step 2:** If BFF NavMap causes issues
- Remove NavMapController registration from DI
- Deploy BFF without NavMap endpoint
- PCF v2.3.0 falls back to hardcoded values (Layer 3)

**Step 3:** Complete rollback (if needed)
- Redeploy previous BFF version
- Keep PCF v2.2.0
- Document issues for future fix

---

## Communication Plan

### Stakeholders to Notify:

1. **Development Team:** Phase 7 starting, task assignments
2. **QA Team:** Test scenarios, expected timeline
3. **DevOps:** Deployment order (BFF first, PCF second)
4. **Business Users:** No user-facing changes, enhanced backend
5. **Support:** New error messages, HOW-TO documentation

### Status Updates:

- Daily standup: Progress on tasks
- Mid-phase checkpoint: After Task 7.2 complete (backend ready)
- Pre-deployment: After Task 7.5 complete (testing done)
- Post-deployment: Metrics review (cache hit rate, errors)

---

## Next Steps

1. **Review this overview** - Confirm approach and decisions
2. **Read task documents** - Detailed implementation steps for each task
3. **Assign task owners** - Backend, Frontend, QA, DevOps
4. **Create feature branch** - `feature/phase-7-navmap-service`
5. **Begin Task 7.1** - Extend IDataverseService

---

**Prepared By:** Claude (AI Agent)
**Date:** 2025-10-19
**Status:** Ready to Begin
**First Task:** TASK-7.1-EXTEND-DATAVERSE-SERVICE.md
