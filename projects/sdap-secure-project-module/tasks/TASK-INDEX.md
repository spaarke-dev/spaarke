# Task Index — Secure Project & External Access Platform

> **Total Tasks**: 40
> **Last Updated**: 2026-03-16

## Status Legend
- 🔲 Not Started
- 🔄 In Progress
- ✅ Completed
- ⏸️ Blocked
- ⏭️ Skipped

## Task Registry

### Phase 1: Data Model & Dataverse Configuration
| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 001 | Create sprk_externalrecordaccess Table | 🔲 | none | dataverse, solution, fields |
| 002 | Add Secure Project Fields to sprk_project | 🔲 | 001 | dataverse, solution, fields |
| 003 | Create Views and Subgrid for External Record Access | 🔲 | 002 | dataverse, solution |
| 004 | Deploy Phase 1 Dataverse Solution to Dev | 🔲 | 003 | deploy, dataverse, solution |

### Phase 2: BFF API — External Access Endpoints
| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 010 | Implement External Caller Authorization Filter | 🔲 | 004 | bff-api, auth, api |
| 011 | Implement Grant External Access Endpoint | 🔲 | 010 | bff-api, api, auth |
| 012 | Implement Revoke External Access Endpoint | 🔲 | 010 | bff-api, api, auth |
| 013 | Implement External User Invitation Endpoint | 🔲 | 010 | bff-api, api |
| 014 | Implement SPE Container Membership Service | 🔲 | 010 | bff-api, api, auth |
| 015 | Implement External User Context Endpoint | 🔲 | 010 | bff-api, api |
| 016 | Implement Project Closure Endpoint | 🔲 | 010 | bff-api, api, auth |
| 017 | Unit Tests for External Access BFF Endpoints | 🔲 | 011, 012, 013 | testing, unit-test, bff-api |
| 018 | Integration Tests for External Access Flows | 🔲 | 017 | testing, integration-test, bff-api |
| 019 | Deploy BFF API Updates to Azure (Phase 2) | 🔲 | 017 | deploy, azure, bff-api |

### Phase 3: Power Pages Configuration
| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 020 | Configure Entra External ID Identity Provider | 🔲 | 019 | auth, config, power-pages |
| 021 | Configure Web Roles and Table Permission Chain | 🔲 | 020 | config, power-pages, auth |
| 022 | Configure Power Pages Web API Site Settings | 🔲 | 021 | config, power-pages |
| 023 | Configure CSP, CORS, and Security Settings | 🔲 | 021 | config, power-pages, security |

### Phase 4: Power Pages Code Page SPA — Foundation
| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 030 | Scaffold Power Pages Code Page SPA Project | 🔲 | 022, 023 | frontend, react, fluent-ui |
| 031 | Implement Portal Auth Module | 🔲 | 030 | frontend, auth, power-pages |
| 032 | Implement BFF API Client Module | 🔲 | 030 | frontend, auth, power-pages |
| 033 | Implement Power Pages Web API Client Module | 🔲 | 031, 032 | frontend, power-pages |
| 034 | Build App Shell with Routing and Error Boundary | 🔲 | 031 | frontend, react, fluent-ui |
| 035 | Create Shared Layout Components | 🔲 | 030 | frontend, react, fluent-ui |
| 036 | Configure Vite Dev Server with Portal Proxy | 🔲 | 030 | frontend, config |

### Phase 5: Power Pages Code Page SPA — Features
| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 040 | Build Workspace Home Page | 🔲 | 033, 034, 035 | frontend, react, fluent-ui |
| 041 | Build Project Page Layout | 🔲 | 033, 034, 035 | frontend, react, fluent-ui |
| 042 | Build Document Library Component | 🔲 | 041 | frontend, react, fluent-ui |
| 043 | Build Events Calendar Component | 🔲 | 041 | frontend, react, fluent-ui |
| 044 | Build Smart To-Do Component | 🔲 | 041 | frontend, react, fluent-ui |
| 045 | Build Contacts and Organizations View | 🔲 | 041 | frontend, react, fluent-ui |
| 046 | Build AI Toolbar (Playbook-Driven) | 🔲 | 042 | frontend, react, fluent-ui, ai |
| 047 | Build Semantic Search Component | 🔲 | 042 | frontend, react, fluent-ui, ai |
| 048 | Build Invite External User Dialog | 🔲 | 041 | frontend, react, fluent-ui |
| 049 | Implement Access Level Enforcement Throughout SPA | 🔲 | 040-048 | frontend, react, auth |
| 050 | Deploy SPA to Power Pages via PAC CLI | 🔲 | 049 | deploy, power-pages, frontend |

### Phase 6: Create Project Wizard Extension
| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 060 | Add Secure Project Toggle to Create Project Wizard | 🔲 | 050 | frontend, react, fluent-ui |
| 061 | Implement BU and Container Provisioning in Wizard | 🔲 | 060 | frontend, bff-api, api |
| 062 | Add External User Invitation Step to Wizard | 🔲 | 061 | frontend, react, fluent-ui |

### Phase 7: Testing, Deployment & Wrap-Up
| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 070 | E2E Test — Secure Project Creation Flow | 🔲 | 062 | testing, e2e-test, integration-test |
| 071 | E2E Test — External User Invitation & Onboarding | 🔲 | 062 | testing, e2e-test |
| 072 | E2E Test — Access Level Enforcement | 🔲 | 062 | testing, e2e-test |
| 073 | E2E Test — Access Revocation Across UAC Planes | 🔲 | 062 | testing, e2e-test |
| 074 | E2E Test — Project Closure Cascading | 🔲 | 062 | testing, e2e-test |
| 075 | Final Deployment — All Components to Dev | 🔲 | 070, 071, 072, 073, 074 | deploy, azure, dataverse, power-pages |
| 090 | Project Wrap-Up | 🔲 | 075 | documentation |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|-------------|-------|
| A | 011, 012, 013 | 010 complete | Independent BFF endpoints (grant, revoke, invite) |
| B | 015, 016 | 010 complete | Independent BFF endpoints (context, closure) |
| C | 022, 023 | 021 complete | Independent Power Pages config (Web API, CSP/CORS) |
| D | 031, 032 | 030 complete | Independent auth modules (portal auth, BFF client) |
| E | 034, 035 | 030 complete | Independent UI components (app shell, layout) |
| F | 040, 041 | 033+034+035 | Home & project page (serial within group) |
| G | 042, 043, 044, 045 | 041 complete | Independent project page components |
| H | 070, 071, 072, 073, 074 | 062 complete | Independent E2E tests |

## Critical Path
001 → 002 → 003 → 004 → 010 → (011,012,013,014) → 017 → 019 → 020 → 021 → (022,023) → 030 → (031,032) → 033 → (034,035) → (040,041) → 042 → 046 → 049 → 050 → 060 → 061 → 062 → (070-074) → 075 → 090

## Estimated Total Hours
| Phase | Tasks | Hours |
|-------|-------|-------|
| Phase 1: Data Model | 4 tasks | 11h |
| Phase 2: BFF API | 10 tasks | 54h |
| Phase 3: Power Pages Config | 4 tasks | 15h |
| Phase 4: SPA Foundation | 7 tasks | 30h |
| Phase 5: SPA Features | 11 tasks | 56h |
| Phase 6: Wizard Extension | 3 tasks | 16h |
| Phase 7: Testing & Deploy | 7 tasks | 28h |
| **Total** | **40 tasks** | **~210h** |
