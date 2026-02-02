# Project Plan: Events and Workflow Automation R1

> **Last Updated**: 2026-02-01
> **Status**: ✅ Complete
> **Completed**: 2026-02-01
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Implement a centralized event management system for Spaarke's legal practice management platform, delivering two reusable platform capabilities: the Association Resolver Framework (addressing Dataverse polymorphic lookup limitations) and the Field Mapping Framework (admin-configurable field inheritance).

**Scope**:
- 5 Dataverse tables (Event, Event Type, Event Log, Field Mapping Profile, Field Mapping Rule)
- 5 PCF controls (AssociationResolver, RegardingLink, EventFormController, FieldMappingAdmin, UpdateRelatedButton)
- 2 API endpoint groups (`/api/v1/events`, `/api/v1/field-mappings`)
- FieldMappingService shared component
- Model-driven app configuration

**Timeline**: 6-8 weeks | **Estimated Effort**: 120-160 hours

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Must use Minimal API pattern, no Azure Functions
- **ADR-006**: Must use PCF controls for all custom UI
- **ADR-008**: Must use endpoint filters for authorization (not global middleware)
- **ADR-010**: DI minimalism (≤15 non-framework registrations)
- **ADR-012**: Must use shared component library for reusable UI
- **ADR-021**: Must use Fluent UI v9, dark mode required, design tokens only
- **ADR-022**: Must use React 16 APIs (`ReactDOM.render`), platform-provided libraries

**From Spec**:
- No Dataverse Business Rules - all validation in code
- Field mapping supports N:1 relationships (not just OOB 1:N)
- Three sync modes: one-time, manual refresh (pull), update related (push)
- Type compatibility in Strict mode for R1 (Resolve mode future)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Dual-field strategy for regarding | Dataverse polymorphic lookups unusable in views | Entity-specific lookups + denormalized fields |
| Two-PCF approach for events | Separation of concerns (selection vs validation) | AssociationResolver + EventFormController |
| Field mappings in Dataverse tables | Admin-configurable without code changes | FieldMappingProfile + FieldMappingRule tables |
| Push API in BFF | Multi-record updates need server-side processing | POST /api/v1/field-mappings/push |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-001-minimal-api.md` - BFF API pattern
- `.claude/adr/ADR-006-pcf-over-webresources.md` - PCF requirement
- `.claude/adr/ADR-008-endpoint-filters.md` - Authorization pattern
- `.claude/adr/ADR-010-di-minimalism.md` - DI constraints
- `.claude/adr/ADR-011-dataset-pcf.md` - Grid control patterns
- `.claude/adr/ADR-012-shared-components.md` - Shared library
- `.claude/adr/ADR-019-problemdetails.md` - Error responses
- `.claude/adr/ADR-021-fluent-design-system.md` - UI requirements
- `.claude/adr/ADR-022-pcf-platform-libraries.md` - React 16 APIs

**Applicable Skills**:
- `.claude/skills/dataverse-deploy/` - PCF and solution deployment
- `.claude/skills/adr-aware/` - Auto-loads relevant ADRs
- `.claude/skills/code-review/` - Quality gates
- `.claude/skills/ui-test/` - PCF control testing

**Applicable Patterns**:
- `.claude/patterns/pcf/control-initialization.md` - PCF lifecycle
- `.claude/patterns/pcf/theme-management.md` - Dark mode
- `.claude/patterns/pcf/dataverse-queries.md` - WebAPI calls
- `.claude/patterns/api/endpoint-definition.md` - Minimal API endpoints
- `.claude/patterns/api/endpoint-filters.md` - Authorization
- `.claude/patterns/api/error-handling.md` - ProblemDetails
- `.claude/patterns/dataverse/web-api-client.md` - Dataverse queries
- `.claude/patterns/testing/integration-tests.md` - API testing

**Scripts Available**:
- `scripts/Deploy-PCFWebResources.ps1` - PCF deployment
- `scripts/Test-SdapBffApi.ps1` - API testing

**Existing Code Examples**:
- `src/client/pcf/ThemeEnforcer/` - Reference PCF control
- `src/server/api/Sprk.Bff.Api/Api/Documents/` - Reference API endpoints

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation & Data Model (Week 1-2)
├─ Create Field Mapping tables in Dataverse
├─ Seed Event Type records
├─ Configure model-driven app forms
└─ Set up PCF project structure

Phase 2: Field Mapping Framework (Week 2-3)
├─ Implement FieldMappingService shared component
├─ Build FieldMappingAdmin PCF control
├─ Create Field Mapping API endpoints
└─ Implement type compatibility validation

Phase 3: Association Resolver (Week 3-4)
├─ Build AssociationResolver PCF control
├─ Integrate with FieldMappingService
├─ Implement regarding field population
└─ Add "Refresh from Parent" functionality

Phase 4: Event Form Controls (Week 4-5)
├─ Build EventFormController PCF
├─ Build RegardingLink PCF
├─ Build UpdateRelatedButton PCF
└─ Configure Event form with controls

Phase 5: API & Event Log (Week 5-6)
├─ Implement Event API endpoints
├─ Implement Event Log tracking
├─ Add push mapping endpoint
└─ Integration testing

Phase 6: Integration & Testing (Week 6-7)
├─ End-to-end testing
├─ Dark mode verification
├─ Performance validation
└─ Documentation

Phase 7: Deployment & Wrap-up (Week 7-8)
├─ Deploy to dev environment
├─ User acceptance testing
├─ Project documentation
└─ Lessons learned
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 (Field Mapping Framework) BLOCKED BY Phase 1 (Dataverse tables)
- Phase 3 (AssociationResolver) BLOCKED BY Phase 2 (FieldMappingService)
- Phase 4 (UpdateRelatedButton) BLOCKED BY Phase 5 (Push API)
- Phase 5 (Event API) can run PARALLEL with Phases 3-4

**High-Risk Items:**
- PCF complexity with 5 controls - Mitigation: Start with simplest (RegardingLink)
- Field mapping cascading logic - Mitigation: Two-pass limit, comprehensive tests

---

## 4. Phase Breakdown

### Phase 1: Foundation & Data Model (Week 1-2)

**Objectives:**
1. Complete Dataverse schema for Field Mapping tables
2. Configure model-driven app forms and views
3. Scaffold PCF project structure

**Deliverables:**
- [ ] Field Mapping Profile table (`sprk_fieldmappingprofile`)
- [ ] Field Mapping Rule table (`sprk_fieldmappingrule`)
- [ ] Seed Event Type records
- [ ] Event form configured with control placeholders
- [ ] Field Mapping admin forms
- [ ] PCF project scaffolds (5 controls)

**Critical Tasks:**
- Create Field Mapping tables FIRST - blocks all mapping work

**Inputs**: spec.md data model, existing Dataverse solution

**Outputs**: Dataverse tables, forms, views, PCF project folders

---

### Phase 2: Field Mapping Framework (Week 2-3)

**Objectives:**
1. Build core mapping service as shared component
2. Implement admin configuration control
3. Create API endpoints for mapping operations

**Deliverables:**
- [ ] FieldMappingService in `@spaarke/ui-components`
- [ ] FieldMappingAdmin PCF control
- [ ] Type compatibility validation logic
- [ ] GET /api/v1/field-mappings/profiles
- [ ] GET /api/v1/field-mappings/profiles/{source}/{target}
- [ ] POST /api/v1/field-mappings/validate

**Critical Tasks:**
- FieldMappingService MUST BE FIRST - used by multiple controls

**Inputs**: Field Mapping tables, PCF patterns

**Outputs**: Shared service, admin PCF, API endpoints

---

### Phase 3: Association Resolver (Week 3-4)

**Objectives:**
1. Build main regarding record selector control
2. Integrate field mapping on record selection
3. Implement refresh functionality

**Deliverables:**
- [ ] AssociationResolver PCF control
- [ ] Entity type dropdown with search
- [ ] Regarding field population logic
- [ ] Field mapping integration (auto-apply on selection)
- [ ] "Refresh from Parent" button
- [ ] Toast notifications for mapping results

**Critical Tasks:**
- Entity configuration must support all 8 entity types

**Inputs**: FieldMappingService, PCF patterns, entity configurations

**Outputs**: AssociationResolver PCF deployed to Event form

---

### Phase 4: Event Form Controls (Week 4-5)

**Objectives:**
1. Build Event Type validation control
2. Build grid link control
3. Build parent push button control

**Deliverables:**
- [ ] EventFormController PCF control
- [ ] Event Type requirement fetching
- [ ] Field show/hide logic
- [ ] Save validation
- [ ] RegardingLink PCF control
- [ ] Clickable navigation
- [ ] UpdateRelatedButton PCF control
- [ ] Confirmation dialog
- [ ] Progress indicator
- [ ] Result toast

**Critical Tasks:**
- EventFormController must block save for invalid data

**Inputs**: Event Type table, FieldMappingService, Push API

**Outputs**: 3 PCF controls deployed

---

### Phase 5: API & Event Log (Week 5-6)

**Objectives:**
1. Implement full Event CRUD API
2. Implement Event Log tracking
3. Add push mapping endpoint

**Deliverables:**
- [ ] GET /api/v1/events (with filters)
- [ ] GET /api/v1/events/{id}
- [ ] POST /api/v1/events
- [ ] PUT /api/v1/events/{id}
- [ ] DELETE /api/v1/events/{id}
- [ ] POST /api/v1/events/{id}/complete
- [ ] POST /api/v1/events/{id}/cancel
- [ ] POST /api/v1/field-mappings/push
- [ ] Event Log creation on state changes
- [ ] Integration tests for all endpoints

**Critical Tasks:**
- Push endpoint needed for UpdateRelatedButton PCF

**Inputs**: Dataverse tables, endpoint patterns, authorization filters

**Outputs**: Complete API layer, Event Log functionality

---

### Phase 6: Integration & Testing (Week 6-7)

**Objectives:**
1. End-to-end testing of all features
2. Dark mode verification for all controls
3. Performance validation

**Deliverables:**
- [ ] E2E test suite for Event creation flow
- [ ] E2E test suite for Field Mapping flow
- [ ] Dark mode verification (all 5 PCF controls)
- [ ] Performance baseline (<200ms PCF render, <500ms API)
- [ ] Bundle size verification (<1MB per control)

**Critical Tasks:**
- ADR-021 dark mode compliance verification

**Inputs**: Deployed controls, API endpoints

**Outputs**: Test reports, performance metrics

---

### Phase 7: Deployment & Wrap-up (Week 7-8)

**Objectives:**
1. Deploy to dev environment
2. User acceptance testing
3. Documentation and lessons learned

**Deliverables:**
- [ ] Solution deployed to dev environment
- [ ] UAT sign-off
- [ ] User documentation
- [ ] Project lessons learned
- [ ] README status updated to Complete

**Critical Tasks:**
- Solution must use unmanaged deployment per ADR-022

**Inputs**: Tested solution, UAT scenarios

**Outputs**: Deployed solution, documentation

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Fluent UI v9 | GA | Low | Platform-provided |
| Dataverse WebAPI | GA | Low | Standard platform |
| React 16/17 (platform) | GA | Low | Platform-provided |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| BFF API infrastructure | `src/server/api/Sprk.Bff.Api/` | Production |
| Existing entity tables | Dataverse | Production |
| Shared UI components | `src/client/shared/Spaarke.UI.Components/` | Production |
| Event tables (Event, Event Type, Event Log) | Dataverse | Created |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- FieldMappingService type compatibility logic
- Entity configuration mapping
- API endpoint handlers

**Integration Tests**:
- Event CRUD operations via API
- Push mapping endpoint with multiple children
- Authorization filter behavior

**E2E Tests**:
- Create Event with regarding record selection
- Field mapping auto-application
- Refresh from Parent flow
- Update Related push flow
- Dark mode toggle verification

**UI Tests** (PCF controls):
- Control renders without console errors
- Dark mode compliance (ADR-021)
- Responsive behavior

---

## 7. Acceptance Criteria

### Phase 1 (Foundation):
- [x] Field Mapping tables created with all fields
- [x] Event form shows control placeholders
- [x] PCF projects scaffold successfully

### Phase 2 (Field Mapping Framework):
- [x] FieldMappingService queries profiles correctly
- [x] Type compatibility rejects incompatible mappings
- [x] API endpoints return expected responses

### Phase 3 (Association Resolver):
- [x] All 8 entity types selectable
- [x] Field mappings auto-apply on selection
- [x] Refresh from Parent updates fields

### Phase 4 (Event Form Controls):
- [x] EventFormController shows/hides fields correctly
- [x] RegardingLink navigates to correct record
- [x] UpdateRelatedButton shows progress and results

### Phase 5 (API & Event Log):
- [x] All Event API endpoints functional
- [x] Event Log records created on state changes
- [x] Push endpoint updates multiple records

### Phase 6 (Integration & Testing):
- [x] All E2E tests passing
- [x] All controls dark mode compliant
- [x] Performance within NFR limits

### Business Acceptance:
- [x] Users can create Events for any supported entity type
- [x] Admins can configure field mappings without code changes
- [x] Events visible in entity subgrids and unified views

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | PCF complexity with 5 controls | Medium | High | Start simple (RegardingLink), reuse patterns |
| R2 | Field mapping cascading loops | Low | High | Two-pass limit in FieldMappingService |
| R3 | React version mismatch | Medium | High | Strict ADR-022 compliance, platform-library |
| R4 | Performance with large push operations | Low | Medium | 500 record limit, pagination |
| R5 | Dataverse query complexity | Medium | Medium | Use indexed fields, limit $expand |

---

## 9. Next Steps

1. **Review this plan.md** with stakeholders
2. **Run** `/task-create projects/events-and-workflow-automation-r1` to generate task files
3. **Create feature branch** and begin Phase 1 implementation

---

**Status**: ✅ Complete
**Completed**: 2026-02-01

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
