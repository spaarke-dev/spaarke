# TASK-INDEX — SDAP SPE Admin App

> **Total Tasks**: 76
> **Phase 1 (MVP)**: 46 tasks
> **Phase 2 (Full Features)**: 23 tasks
> **Phase 3 (Advanced)**: 7 tasks

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Complete |
| ⛔ | Blocked |

---

## Phase 1: MVP — Foundation & Core Features

### 1.1 Dataverse Tables & Configuration

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 001 | Create sprk_speenvironment table schema | ✅ | — | 2h |
| 002 | Create sprk_specontainertypeconfig table schema | ✅ | 001 | 3h |
| 003 | Create sprk_speauditlog table schema | ✅ | 001, 002 | 2h |
| 004 | Add SPE Admin sitemap entry | ✅ | — | 1h |

### 1.2 BFF API — Infrastructure

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 005 | Create SpeAdminModule DI feature module | 🔲 | — | 2h |
| 006 | Create SpeAdminGraphService (multi-config Graph client) | 🔲 | 005 | 4h |
| 007 | Create SpeAuditService (audit logging) | 🔲 | 005 | 2h |
| 008 | Create SpeAdminAuthorizationFilter | 🔲 | 005 | 2h |
| 009 | Create SpeAdminEndpoints route group registration | 🔲 | 005, 008 | 1h |

### 1.3 BFF API — Configuration Endpoints

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 010 | Environment CRUD endpoints | 🔲 | 009, 007 | 3h |
| 011 | Container type config CRUD endpoints | 🔲 | 009, 007 | 3h |
| 012 | Business Unit list endpoint | 🔲 | 009 | 1h |

### 1.4 BFF API — Container Endpoints

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 013 | List/Get containers endpoints | 🔲 | 006, 009 | 2h |
| 014 | Create container endpoint | 🔲 | 013, 007 | 2h |
| 015 | Update/Activate/Lock/Unlock container endpoints | 🔲 | 013, 007 | 3h |
| 016 | Container permission CRUD endpoints | 🔲 | 013, 007 | 3h |

### 1.5 BFF API — File Endpoints

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 017 | List items in container endpoint | 🔲 | 006, 009 | 2h |
| 018 | Upload file endpoint | 🔲 | 017, 007 | 3h |
| 019 | Download/Preview/Delete file endpoints | 🔲 | 017, 007 | 2h |
| 020 | File versions/thumbnails/sharing endpoints | 🔲 | 017 | 2h |
| 021 | Create folder endpoint | 🔲 | 017 | 1h |

### 1.6 BFF API — Dashboard & Audit

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 022 | Dashboard metrics + SpeDashboardSyncService | 🔲 | 006, 009 | 3h |
| 023 | Audit log query endpoint | 🔲 | 009, 007 | 2h |

### 1.7 Code Page — Project Scaffolding

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 024 | Scaffold SpeAdminApp Code Page project | 🔲 | — | 2h |
| 025 | Create main.tsx entry point | 🔲 | 024 | 1h |
| 026 | Create TypeScript types (spe.ts) | 🔲 | 024 | 2h |
| 027 | Create speApiClient service | 🔲 | 024 | 2h |
| 028 | Create BuContext state management | 🔲 | 024, 026 | 2h |

### 1.8 Code Page — Layout & Navigation

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 029 | Create AppShell layout component | 🔲 | 025, 028 | 3h |
| 030 | Create BuContextPicker cascade component | 🔲 | 029, 027 | 3h |
| 031 | Create NavigationPanel component | 🔲 | 029 | 2h |

### 1.9 Code Page — Dashboard

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 032 | Create DashboardPage | 🔲 | 029, 027, 028 | 3h |

### 1.10 Code Page — Container Management

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 033 | Create ContainersPage | 🔲 | 029, 027, 028 | 3h |
| 034 | Create ContainerDetail side panel | 🔲 | 033 | 3h |
| 035 | Create PermissionPanel component | 🔲 | 034 | 3h |

### 1.11 Code Page — File Browser

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 036 | Create FileBrowserPage | 🔲 | 029, 027, 028 | 4h |
| 037 | Create FileDetailPanel | 🔲 | 036 | 3h |

### 1.12 Code Page — Settings

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 038 | Create SettingsPage | 🔲 | 029, 027 | 3h |
| 039 | Create EnvironmentConfig component | 🔲 | 038 | 2h |
| 040 | Create ContainerTypeConfig component | 🔲 | 038 | 3h |

### 1.13 Code Page — Audit Log

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 041 | Create AuditLogPage | 🔲 | 029, 027 | 3h |

### 1.14 Phase 1 Integration & Deploy

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 042 | Build Code Page and verify | 🔲 | 032-041 | 2h |
| 043 | Deploy BFF API with SPE endpoints | 🔲 | 010-023 | 2h |
| 044 | Deploy Code Page to Dataverse | 🔲 | 042 | 2h |
| 045 | Create Dataverse tables in environment | ✅ | 001-003 | 2h |
| 046 | Phase 1 end-to-end integration testing | 🔲 | 042-045 | 4h |

---

## Phase 2: Full Features

### 2.1 BFF API — Container Type Endpoints

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 050 | List/Get container types endpoints | 🔲 | 006, 009 | 2h |
| 051 | Create container type endpoint | 🔲 | 050, 007 | 2h |
| 052 | Update container type settings endpoint | 🔲 | 050 | 2h |
| 053 | Register container type endpoint | 🔲 | 050 | 3h |
| 054 | List container type app permissions endpoint | 🔲 | 050 | 2h |

### 2.2 BFF API — Additional Endpoints

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 055 | Container column CRUD endpoints | 🔲 | 013 | 2h |
| 056 | Container custom property endpoints | 🔲 | 013 | 2h |
| 057 | Search containers endpoint | 🔲 | 006, 009 | 3h |
| 058 | Search items endpoint | 🔲 | 006, 009 | 3h |
| 059 | Recycle bin endpoints | 🔲 | 013, 007 | 2h |
| 060 | Security alerts and score endpoints | 🔲 | 009 | 2h |

### 2.3 Code Page — Container Types UI

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 061 | Create ContainerTypesPage UI | 🔲 | 029, 050-054 | 3h |
| 062 | Create ContainerTypeDetail panel | 🔲 | 061 | 3h |
| 063 | Create RegisterWizard | 🔲 | 061 | 4h |

### 2.4 Code Page — Container Enhancements

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 064 | Create ColumnEditor | 🔲 | 034 | 3h |
| 065 | Create CustomPropertyEditor | 🔲 | 034 | 3h |

### 2.5 Code Page — Search

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 066 | Create SearchPage | 🔲 | 029, 057, 058 | 3h |
| 067 | Create ContainerResultsGrid | 🔲 | 066 | 3h |
| 068 | Create ItemResultsGrid | 🔲 | 066 | 3h |

### 2.6 Code Page — Recycle Bin & Security

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 069 | Create RecycleBinPage | 🔲 | 029, 059 | 2h |
| 070 | Create SecurityPage | 🔲 | 029, 060 | 3h |

### 2.7 Phase 2 Integration & Deploy

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 071 | Phase 2 integration testing | 🔲 | 061-070 | 3h |
| 072 | Deploy updated BFF and Code Page | 🔲 | 071 | 2h |

---

## Phase 3: Advanced Features

| # | Task | Status | Deps | Est |
|---|------|--------|------|-----|
| 080 | eDiscovery read-only dashboard | 🔲 | 072 | 4h |
| 081 | Retention label management | 🔲 | 072 | 3h |
| 082 | Multi-tenant consuming tenant management | 🔲 | 072 | 4h |
| 083 | Bulk operations | 🔲 | 072 | 4h |
| 084 | Multi-app registration support | 🔲 | 072 | 4h |
| 085 | Phase 3 integration testing and deployment | 🔲 | 080-084 | 3h |
| 090 | Project wrap-up | 🔲 | 085 | 2h |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 004, 005, 024 | None | Independent starting points (Dataverse, BFF, Code Page) |
| B | 006, 007, 008 | 005 | BFF infrastructure services (independent of each other) |
| C | 010, 011, 012 | 009 | Configuration endpoints (independent) |
| D | 013, 017, 022, 023 | 006+009 | Independent API endpoint groups |
| E | 025, 026, 027 | 024 | Code Page scaffolding files (independent) |
| F | 032, 033, 036, 038, 041 | 029+027 | Independent UI page components |
| G | 050, 055, 056, 057, 058, 059, 060 | Phase 1 complete | Phase 2 API endpoints (mostly independent) |
| H | 064, 065 | 034 | Container detail tabs (independent) |
| I | 067, 068 | 066 | Search result grids (independent) |
| J | 080, 081, 082, 083, 084 | 072 | Phase 3 features (independent) |

## Critical Path

```
005 → 006 → 009 → 013 → 024 → 025 → 028 → 029 → 033 → 042 → 046
(DI)  (Graph) (Routes) (API)  (Scaffold) (Entry) (Context) (Shell) (Containers) (Build) (E2E)
```

## Estimated Effort Summary

| Phase | Tasks | Total Hours |
|-------|-------|-------------|
| Phase 1 (MVP) | 001-046 | ~109h |
| Phase 2 (Full) | 050-072 | ~55h |
| Phase 3 (Advanced) | 080-090 | ~24h |
| **Total** | **76 tasks** | **~188h** |

---

*Generated by project-pipeline. Updated: 2026-03-13*
