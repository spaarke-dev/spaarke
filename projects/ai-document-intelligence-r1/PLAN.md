# Project Plan: AI Document Intelligence R1 - Core Infrastructure

> **Last Updated**: 2025-12-25
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)
> **Version**: 2.0 (Rescoped)

---

## 1. Executive Summary

**Purpose**: Establish the core infrastructure foundation for the AI Analysis feature by verifying existing code works, creating missing Dataverse entities, validating environment variables, and testing deployment pipelines.

**Scope**:
- Verify 10 Dataverse entities exist (create if missing)
- Verify BFF API endpoints function correctly with SSE streaming
- Validate environment variable resolution
- Test AI Foundry Hub connections
- Create security roles and export solution
- Test deployment to external environment
- Create Phase 1 deployment guide

**Estimated Effort**: 15-25 tasks (conditional on verification results)

**R1 Focus**: This is a **verification-first** release. Most code already exists - the goal is to validate it works correctly and fill in any gaps.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Minimal API pattern for all endpoints
- **ADR-003**: Lean Authorization with endpoint filters
- **ADR-007**: SpeFileStore facade for all file access
- **ADR-008**: Per-resource authorization filters
- **ADR-010**: DI Minimalism (max 15 non-framework registrations)
- **ADR-013**: AI Architecture - AI features extend BFF API

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Verification before creation | Code exists but entities status unknown | Prevents duplicate work |
| Conditional entity creation | Only create if verification fails | Saves time if entities exist |
| Environment Variables in Dataverse | Enables multi-tenant deployment | Zero hard-coded config |

### Discovered Resources

**Applicable ADRs**:
- `docs/adr/ADR-001-minimal-api-and-workers.md` - Minimal API pattern
- `docs/adr/ADR-008-authorization-endpoint-filters.md` - Endpoint filters
- `docs/adr/ADR-013-ai-architecture.md` - AI feature architecture

**Applicable Skills**:
- `.claude/skills/dataverse-deploy/` - Solution deployment
- `.claude/skills/adr-aware/` - Automatic ADR loading

**Knowledge Articles**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` - AI implementation guide
- `docs/guides/AI-IMPLEMENTATION-STATUS.md` - Current AI status

**Reusable Scripts**:
- `scripts/Test-SdapBffApi.ps1` - API validation

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1A: Verification (Priority 1)
└─ Verify Dataverse entities exist in dev
└─ Verify BFF API endpoints work (health, SSE)
└─ Verify environment variables resolve
└─ Verify AI Foundry connections work

Phase 1B: Entity Creation (Conditional)
└─ Only execute if Phase 1A verification fails
└─ Create missing Dataverse entities
└─ Create security roles
└─ Export solution package

Phase 1C: Deployment Testing
└─ Test Bicep deployment to external subscription
└─ Test Dataverse solution import
└─ Run integration tests
└─ Create deployment guide
```

### Critical Path

**Blocking Dependencies:**
- Phase 1B BLOCKED BY Phase 1A verification results
- Phase 1C BLOCKED BY Phase 1A (need working infrastructure)

**High-Risk Items:**
- Dataverse entity status unknown (may need creation)
- AI Foundry integration not fully tested
- Environment variable resolution in deployed API

---

## 4. Phase Breakdown

### Phase 1A: Verification (Priority 1)

**Objectives:**
1. Determine if Dataverse entities already exist
2. Verify BFF API endpoints function correctly
3. Validate environment variable resolution
4. Test AI Foundry Hub connectivity

**Deliverables:**
- [ ] Dataverse entity verification report (10 entities checked)
- [ ] API health check results (/ping, /healthz)
- [ ] SSE streaming endpoint test results
- [ ] Environment variable resolution log
- [ ] AI Foundry connection test results

**Tasks:**
| ID | Task | Est. Hours | Dependencies |
|----|------|------------|--------------|
| 001 | Verify Dataverse entities exist | 2 | none |
| 002 | Verify Environment Variables in solution | 2 | none |
| 003 | Verify AI Foundry Hub connections | 2 | none |
| 004 | Run API health check and SSE test | 2 | none |
| 005 | Document verification results | 1 | 001-004 |

**Inputs**: PAC CLI access, Azure portal access, API deployment

**Outputs**: Verification report, list of missing entities (if any)

### Phase 1B: Entity Creation (Conditional)

**Objectives:**
1. Create any missing Dataverse entities
2. Configure entity relationships
3. Create security roles
4. Export managed solution

**Only execute tasks where verification failed.**

**Deliverables:**
- [ ] Missing entities created (if any)
- [ ] Security roles: Analysis User, Analysis Admin
- [ ] Exported managed solution package

**Conditional Tasks:**
| ID | Task | Est. Hours | Condition |
|----|------|------------|-----------|
| 010 | Create sprk_analysis entity | 4 | If missing |
| 011 | Create sprk_analysisaction entity | 2 | If missing |
| 012 | Create sprk_analysisskill entity | 2 | If missing |
| 013 | Create sprk_analysisknowledge entity | 3 | If missing |
| 014 | Create sprk_knowledgedeployment entity | 3 | If missing |
| 015 | Create sprk_analysistool entity | 2 | If missing |
| 016 | Create sprk_analysisplaybook entity | 4 | If missing |
| 017 | Create sprk_analysisworkingversion entity | 3 | If missing |
| 018 | Create sprk_analysisemailmetadata entity | 2 | If missing |
| 019 | Create sprk_analysischatmessage entity | 2 | If missing |
| 020 | Create security roles | 3 | Always (if any entities created) |
| 021 | Export solution package | 2 | After entity creation |

**Inputs**: Entity designs from CODE-INVENTORY.md, existing BFF code references

**Outputs**: Dataverse entities, security roles, managed solution

### Phase 1C: Deployment Testing

**Objectives:**
1. Validate infrastructure deployment works
2. Test solution import to clean environment
3. Run integration tests
4. Document deployment procedure

**Deliverables:**
- [ ] Bicep deployment to test subscription
- [ ] Solution import to test org
- [ ] Integration test results
- [ ] Phase 1 Deployment Guide

**Tasks:**
| ID | Task | Est. Hours | Dependencies |
|----|------|------------|--------------|
| 030 | Test Bicep deployment to external subscription | 4 | Phase 1A/1B |
| 031 | Test Dataverse solution import to clean env | 3 | 021 |
| 032 | Verify environment variables resolve in deployed API | 2 | 030 |
| 033 | Run integration tests against dev | 3 | 004, 030 |
| 034 | Create Phase 1 deployment guide | 4 | 030-033 |
| 090 | Project wrap-up | 2 | All tasks |

**Inputs**: Phase 1A/1B deliverables, test subscription

**Outputs**: Deployment validation, deployment guide, lessons learned

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Dataverse dev environment | Available | Low | spaarkedev1.crm.dynamics.com |
| Azure AI Foundry Hub | Deployed | Low | sprkspaarkedev-aif-hub |
| Azure OpenAI | Deployed | Low | spaarke-openai-dev |
| BFF API | Deployed | Low | spe-api-dev-67e2xz |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| BFF API Code | `src/server/api/Sprk.Bff.Api/` | COMPLETE |
| Unit Tests | `tests/unit/Sprk.Bff.Api.Tests/` | COMPLETE |
| Infrastructure Bicep | `infrastructure/bicep/` | EXISTS |

---

## 6. Testing Strategy

**Verification Tests** (Phase 1A):
- Entity existence checks via PAC CLI
- API health endpoints (/ping, /healthz)
- SSE streaming with test payload

**Integration Tests** (Phase 1C):
- End-to-end API flow with real AI response
- Solution import validation
- Environment variable resolution

---

## 7. Acceptance Criteria

### Phase 1A (Verification)
- [ ] All 10 Dataverse entities verified (exist or confirmed missing)
- [ ] API health check returns 200
- [ ] SSE streaming works for /execute endpoint
- [ ] Environment variables documented

### Phase 1B (Entity Creation - if needed)
- [ ] All missing entities created with correct schema
- [ ] Security roles grant appropriate access
- [ ] Solution exports without errors

### Phase 1C (Deployment Testing)
- [ ] Bicep deploys to external subscription
- [ ] Solution imports to clean environment
- [ ] Integration tests pass
- [ ] Deployment guide created and validated

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | All entities missing | Medium | High | Allocate full Phase 1B if needed |
| R2 | AI Foundry not connected | Low | Medium | Template exists, may need wiring |
| R3 | Env vars not resolving | Low | Medium | Check appsettings.json pattern |
| R4 | Solution import fails | Low | High | Debug before Phase 1C |

---

## 9. Next Steps

1. **Run** task creation to generate POML task files
2. **Begin** with Task 001 (Verify Dataverse entities)
3. **Branch** based on verification results (Phase 1B if needed)

---

## 10. Existing Code (DO NOT RECREATE)

See [spec.md](spec.md#existing-implementation-do-not-recreate) for full inventory of:
- BFF API endpoints (COMPLETE)
- BFF Services (COMPLETE)
- BFF Models (COMPLETE)
- Unit Tests (COMPLETE)
- Infrastructure (PARTIAL)

**R1 Task Type Guidelines:**
| Existing Status | Task Type |
|-----------------|-----------|
| COMPLETE | Verify only |
| EXISTS | Verify + Complete |
| TEMPLATE ONLY | Complete |
| STATUS UNKNOWN | Verify + Create |

---

**Status**: Ready for Tasks
**Next Action**: Generate task files

---

*For Claude Code: This plan is for R1 (Core Infrastructure). UI components are in R2, advanced features in R3.*
