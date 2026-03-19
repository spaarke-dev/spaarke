# Task Index: Production Environment Setup R2

> **Last Updated**: 2026-03-18
> **Total Tasks**: 38
> **Status**: 0/38 complete

---

## Status Legend
- 🔲 Not Started
- 🔄 In Progress
- ✅ Complete
- ⛔ Blocked

---

## Phase 1: Foundation (Tasks 001-005)

| # | Task | Status | Parallel Group | Dependencies | Est. |
|---|------|--------|---------------|--------------|------|
| 001 | Fix OfficeDocumentPersistence.cs hardcoded values | 🔲 | 1A | none | 2h |
| 002 | Fix OfficeService.cs hardcoded values | 🔲 | 1A | none | 2h |
| 003 | Fix appsettings CORS and add tokens | 🔲 | 1A | none | 1h |
| 004 | Add Dataverse env var definitions to solution XML | 🔲 | 1B | none | 3h |
| 005 | Parameterize infrastructure scripts | 🔲 | 1B | none | 2h |

## Phase 2: Shared Library Core (Tasks 010-012) — CRITICAL PATH

| # | Task | Status | Parallel Group | Dependencies | Est. |
|---|------|--------|---------------|--------------|------|
| 010 | Add resolveRuntimeConfig() to @spaarke/auth | 🔲 | 2 | Phase 1 | 4h |
| 011 | Clean environmentVariables.ts defaults | 🔲 | 2 | none | 2h |
| 012 | Standardize window globals + scope format | 🔲 | 2 | 010 | 2h |

## Phase 3: Code Page Migration (Tasks 020-028) — ALL PARALLEL

| # | Task | Status | Parallel Group | Dependencies | Est. |
|---|------|--------|---------------|--------------|------|
| 020 | Migrate AnalysisWorkspace (pilot) | 🔲 | 3 | 010 | 3h |
| 021 | Migrate PlaybookBuilder | 🔲 | 3 | 010 | 2h |
| 022 | Migrate SprkChatPane | 🔲 | 3 | 010 | 2h |
| 023 | Migrate LegalWorkspace | 🔲 | 3 | 010 | 2h |
| 024 | Migrate DocumentUploadWizard | 🔲 | 3 | 010 | 2h |
| 025 | Migrate SpeAdminApp | 🔲 | 3 | 010 | 2h |
| 026 | Migrate DocumentRelationshipViewer (code page) | 🔲 | 3 | 010 | 2h |
| 027 | Migrate SemanticSearch (code page) | 🔲 | 3 | 010 | 2h |
| 028 | Migrate External SPA | 🔲 | 3 | 010 | 2h |

## Phase 4: PCF Control Migration (Tasks 030-037) — ALL PARALLEL

| # | Task | Status | Parallel Group | Dependencies | Est. |
|---|------|--------|---------------|--------------|------|
| 030 | Migrate UniversalQuickCreate PCF | 🔲 | 4 | 011 | 2h |
| 031 | Migrate DocumentRelationshipViewer PCF | 🔲 | 4 | 011 | 2h |
| 032 | Migrate SemanticSearchControl PCF | 🔲 | 4 | 011 | 2h |
| 033 | Migrate RelatedDocumentCount PCF | 🔲 | 4 | 011 | 2h |
| 034 | Migrate UniversalDatasetGrid PCF | 🔲 | 4 | 011 | 2h |
| 035 | Migrate EmailProcessingMonitor PCF | 🔲 | 4 | 011 | 2h |
| 036 | Migrate AssociationResolver PCF | 🔲 | 4 | 011 | 2h |
| 037 | Migrate ScopeConfigEditor PCF | 🔲 | 4 | 011 | 2h |

## Phase 5: Legacy JS + Office Add-ins (Tasks 040-046) — ALL PARALLEL

| # | Task | Status | Parallel Group | Dependencies | Est. |
|---|------|--------|---------------|--------------|------|
| 040 | Fix sprk_subgrid_parent_rollup.js | 🔲 | 5 | 010 | 2h |
| 041 | Fix sprk_emailactions.js | 🔲 | 5 | 010 | 1h |
| 042 | Fix sprk_DocumentOperations.js | 🔲 | 5 | 010 | 1h |
| 043 | Fix sprk_communication_send.js | 🔲 | 5 | 010 | 1h |
| 044 | Fix sprk_aichatcontextmap_ribbon.js | 🔲 | 5 | 010 | 1h |
| 045 | Parameterize Office add-in auth config | 🔲 | 5 | 010 | 3h |
| 046 | Fix ribbon webresource JS files | 🔲 | 5 | 010 | 2h |

## Phase 6: Validation & Cleanup (Tasks 050-090)

| # | Task | Status | Parallel Group | Dependencies | Est. |
|---|------|--------|---------------|--------------|------|
| 050 | Create Validate-DeployedEnvironment.ps1 | 🔲 | 6 | All Phase 1-5 | 4h |
| 051 | Update Provision-Customer.ps1 | 🔲 | 6 | 004, 050 | 2h |
| 052 | Batch parameterize remaining scripts | 🔲 | 6 | 005 | 4h |
| 053 | Update deployment documentation | 🔲 | 6 | 050, 051 | 2h |
| 090 | Project wrap-up | 🔲 | 6-final | All | 2h |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Max Agents | Notes |
|-------|-------|--------------|------------|-------|
| 1A | 001, 002, 003 | None | 3 | BFF API files — independent C# changes |
| 1B | 004, 005 | None | 2 | Dataverse + infra — independent of BFF |
| 2 | 010, 011, 012 | Phase 1 | 2-3 | Shared libs — 012 depends on 010 |
| 3 | 020-028 | Task 010 | Up to 9 | All code pages — fully independent |
| 4 | 030-037 | Task 011 | Up to 8 | All PCF controls — fully independent |
| 5 | 040-046 | Task 010 | Up to 7 | Legacy JS + add-ins — fully independent |
| 6 | 050-053 | Phase 1-5 | 2-3 | Validation + cleanup — some sequential |
| 6-final | 090 | All | 1 | Must be last |

## Critical Path

```
Phase 1 (Groups 1A+1B, parallel) → Phase 2 (Task 010 CRITICAL) → Phase 3+4+5 (ALL PARALLEL) → Phase 6 → 090
```

**Estimated critical path duration**: ~20 hours sequential, ~10 hours with full parallelism

## Dependency Graph

```
001 ─┐
002 ─┤
003 ─┼─→ Phase 1 complete ─→ 010 ─→ 020-028 (parallel)
004 ─┤                       ├──→ 040-046 (parallel)
005 ─┘                       └──→ 012

                              011 ─→ 030-037 (parallel)

                              All ─→ 050 ─→ 051 ─→ 053
                              005 ─→ 052
                              All ─→ 090
```

---

*Total estimated effort: ~72 hours sequential / ~25 hours with full parallelism (8+ concurrent agents)*
