# SDAP Office Integration - Task Index

> **Auto-generated**: 2026-01-20
> **Total Tasks**: 56
> **Status**: Ready for Execution

---

## Status Legend

| Symbol | Status |
|--------|--------|
| 🔲 | Not Started |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Blocked |
| ⏭️ | Deferred |

---

## Phase 1: Foundation & Setup (001-006)

| ID | Task | Status | Dependencies | Rigor |
|----|------|--------|--------------|-------|
| 001 | [Create Azure AD app registration for Office add-in](001-create-addin-app-registration.poml) | 🔲 | none | STANDARD |
| 002 | [Update BFF API app registration for OBO](002-update-bff-app-registration.poml) | 🔲 | 001 | STANDARD |
| 003 | [Create Office add-in project structure](003-create-addin-project-structure.poml) | 🔲 | none | FULL |
| 004 | [Create Outlook unified manifest](004-create-outlook-manifest.poml) | 🔲 | 003 | STANDARD |
| 005 | [Create Word XML manifest](005-create-word-manifest.poml) | 🔲 | 003 | STANDARD |
| 006 | [Create shared task pane React project](006-create-shared-taskpane.poml) | 🔲 | 003 | FULL |

---

## Phase 2: Dataverse Schema (010-016)

| ID | Task | Status | Dependencies | Rigor |
|----|------|--------|--------------|-------|
| 010 | [Create EmailArtifact table](010-create-emailartifact-table.poml) | 🔲 | none | STANDARD |
| 011 | [Create AttachmentArtifact table](011-create-attachmentartifact-table.poml) | 🔲 | 010 | STANDARD |
| 012 | [Create ProcessingJob table (ADR-004)](012-create-processingjob-table.poml) | 🔲 | none | STANDARD |
| 013 | [Configure table relationships and indexes](013-configure-table-relationships.poml) | 🔲 | 010, 011, 012 | STANDARD |
| 014 | [Configure security roles](014-configure-security-roles.poml) | 🔲 | 013 | STANDARD |
| 015 | [Deploy Dataverse solution](015-deploy-dataverse-solution.poml) | 🔲 | 014 | STANDARD |

---

## Phase 3: Backend API Development (020-036)

| ID | Task | Status | Dependencies | Rigor |
|----|------|--------|--------------|-------|
| 020 | [Create Office endpoints module structure](020-create-office-endpoints-module.poml) | 🔲 | 015 | FULL |
| 021 | [Implement POST /office/save endpoint](021-implement-save-endpoint.poml) | 🔲 | 020 | FULL |
| 022 | [Implement GET /office/jobs/{id} endpoint](022-implement-job-status-endpoint.poml) | 🔲 | 020 | FULL |
| 023 | [Implement GET /office/jobs/{id}/stream SSE endpoint](023-implement-sse-endpoint.poml) | 🔲 | 022 | FULL |
| 024 | [Implement GET /office/search/entities endpoint](024-implement-entity-search-endpoint.poml) | 🔲 | 020 | FULL |
| 025 | [Implement GET /office/search/documents endpoint](025-implement-document-search-endpoint.poml) | 🔲 | 020 | FULL |
| 026 | [Implement POST /office/quickcreate/{entityType} endpoint](026-implement-quickcreate-endpoint.poml) | 🔲 | 020 | FULL |
| 027 | [Implement POST /office/share/links endpoint](027-implement-share-links-endpoint.poml) | 🔲 | 020 | FULL |
| 028 | [Implement POST /office/share/attach endpoint](028-implement-share-attach-endpoint.poml) | 🔲 | 020 | FULL |
| 029 | [Implement GET /office/recent endpoint](029-implement-recent-endpoint.poml) | 🔲 | 020 | FULL |
| 030 | [Implement idempotency middleware](030-implement-idempotency-middleware.poml) | 🔲 | 021 | FULL |
| 031 | [Implement rate limiting](031-implement-rate-limiting.poml) | 🔲 | 020 | FULL |
| 032 | [Implement error handling (ProblemDetails)](032-implement-error-handling.poml) | 🔲 | 020 | FULL |
| 033 | [Implement authorization filters](033-implement-authorization-filters.poml) | 🔲 | 020 | FULL |
| 034 | [Create API unit tests](034-create-api-unit-tests.poml) | 🔲 | 021-033 | STANDARD |
| 035 | [Deploy BFF API to Azure](035-deploy-bff-api.poml) | 🔲 | 034 | STANDARD |

---

## Phase 4: Office Add-in Development (040-058)

| ID | Task | Status | Dependencies | Rigor |
|----|------|--------|--------------|-------|
| 040 | [Implement NAA authentication service](040-implement-naa-auth-service.poml) | 🔲 | 006, 001 | FULL |
| 041 | [Implement Dialog API auth fallback](041-implement-dialog-auth-fallback.poml) | 🔲 | 040 | FULL |
| 042 | [Create host adapter interface](042-create-host-adapter-interface.poml) | 🔲 | 006 | FULL |
| 043 | [Implement Outlook adapter](043-implement-outlook-adapter.poml) | 🔲 | 042 | FULL |
| 044 | [Implement Word adapter](044-implement-word-adapter.poml) | 🔲 | 042 | FULL |
| 045 | [Create shared API client service](045-create-api-client-service.poml) | 🔲 | 040, 035 | FULL |
| 046 | [Create task pane shell with FluentProvider](046-create-taskpane-shell.poml) | 🔲 | 006 | FULL |
| 047 | [Implement entity picker component](047-implement-entity-picker.poml) | 🔲 | 046 | FULL |
| 048 | [Implement attachment selector component](048-implement-attachment-selector.poml) | 🔲 | 046 | FULL |
| 049 | [Implement save flow UI](049-implement-save-flow-ui.poml) | 🔲 | 047, 048 | FULL |
| 050 | [Implement share flow UI](050-implement-share-flow-ui.poml) | 🔲 | 046 | FULL |
| 051 | [Implement Quick Create dialog](051-implement-quickcreate-dialog.poml) | 🔲 | 046 | FULL |
| 052 | [Implement job status component (SSE + polling)](052-implement-job-status-component.poml) | 🔲 | 046, 023 | FULL |
| 053 | [Implement error handling and notifications](053-implement-error-notifications.poml) | 🔲 | 046 | FULL |
| 054 | [Implement dark mode and high-contrast support](054-implement-dark-mode-support.poml) | 🔲 | 046 | FULL |
| 055 | [Implement accessibility (keyboard nav, screen reader)](055-implement-accessibility.poml) | 🔲 | 046 | FULL |
| 056 | [Create add-in unit tests](056-create-addin-unit-tests.poml) | 🔲 | 040-055 | STANDARD |
| 057 | [Deploy Outlook add-in to M365 admin center](057-deploy-outlook-addin.poml) | 🔲 | 056 | STANDARD |
| 058 | [Deploy Word add-in](058-deploy-word-addin.poml) | 🔲 | 056 | STANDARD |

---

## Phase 5: Background Workers (060-066)

| ID | Task | Status | Dependencies | Rigor |
|----|------|--------|--------------|-------|
| 060 | [Create worker project structure](060-create-worker-project-structure.poml) | 🔲 | 020 | FULL |
| 061 | [Implement upload finalization worker](061-implement-upload-worker.poml) | 🔲 | 060 | FULL |
| 062 | [Implement profile summary worker](062-implement-profile-worker.poml) | 🔲 | 060 | FULL |
| 063 | [Implement indexing worker](063-implement-indexing-worker.poml) | 🔲 | 060 | FULL |
| 064 | [Implement job status update service](064-implement-job-status-service.poml) | 🔲 | 060, 023 | FULL |
| 065 | [Create worker unit tests](065-create-worker-unit-tests.poml) | 🔲 | 061-064 | STANDARD |
| 066 | [Deploy workers to Azure](066-deploy-workers.poml) | 🔲 | 065 | STANDARD |

---

## Phase 6: Integration & Testing (070-078)

| ID | Task | Status | Dependencies | Rigor |
|----|------|--------|--------------|-------|
| 070 | [E2E test: Outlook save flow](070-e2e-outlook-save-flow.poml) | 🔲 | 057, 066 | STANDARD |
| 071 | [E2E test: Word save flow](071-e2e-word-save-flow.poml) | 🔲 | 058, 066 | STANDARD |
| 072 | [E2E test: Share flow](072-e2e-share-flow.poml) | 🔲 | 057 | STANDARD |
| 073 | [E2E test: Quick Create flow](073-e2e-quickcreate-flow.poml) | 🔲 | 057 | STANDARD |
| 074 | [Integration test: SSE job status](074-integration-sse-jobstatus.poml) | 🔲 | 066 | STANDARD |
| 075 | [Integration test: Duplicate detection](075-integration-duplicate-detection.poml) | 🔲 | 035 | STANDARD |
| 076 | [Accessibility audit](076-accessibility-audit.poml) | 🔲 | 055 | MINIMAL |
| 077 | [Performance testing](077-performance-testing.poml) | 🔲 | 035, 066 | STANDARD |
| 078 | [Security review](078-security-review.poml) | 🔲 | 035, 057 | STANDARD |

---

## Phase 7: Deployment & Go-Live (080-084)

| ID | Task | Status | Dependencies | Rigor |
|----|------|--------|--------------|-------|
| 080 | [Production deployment: BFF API](080-production-deploy-bff.poml) | 🔲 | 078 | STANDARD |
| 081 | [Production deployment: Add-ins](081-production-deploy-addins.poml) | 🔲 | 080 | STANDARD |
| 082 | [Create user documentation](082-create-user-documentation.poml) | 🔲 | 081 | MINIMAL |
| 083 | [Create admin documentation](083-create-admin-documentation.poml) | 🔲 | 081 | MINIMAL |
| 084 | [Configure monitoring and alerting](084-configure-monitoring.poml) | 🔲 | 080 | STANDARD |

---

## Wrap-up (090)

| ID | Task | Status | Dependencies | Rigor |
|----|------|--------|--------------|-------|
| 090 | [Project wrap-up](090-project-wrap-up.poml) | 🔲 | 080-084 | FULL |

---

## Summary

| Phase | Tasks | Effort Estimate |
|-------|-------|-----------------|
| 1: Foundation & Setup | 6 | 5-7 days |
| 2: Dataverse Schema | 6 | 3-4 days |
| 3: Backend API | 16 | 10-14 days |
| 4: Office Add-in | 19 | 12-16 days |
| 5: Background Workers | 7 | 5-7 days |
| 6: Integration & Testing | 9 | 6-8 days |
| 7: Deployment & Go-Live | 5 | 3-4 days |
| Wrap-up | 1 | 1 day |
| **Total** | **56** | **45-60 days** |

---

## Rigor Level Distribution

| Level | Count | Description |
|-------|-------|-------------|
| FULL | 35 | Code implementation, architecture changes |
| STANDARD | 19 | Tests, deployment, configuration |
| MINIMAL | 2 | Documentation only |

---

## Execution Order Recommendation

1. **Start with Phase 1** - Foundation tasks (001-006) have minimal dependencies
2. **Phase 2 can start after 001** - Schema work is independent
3. **Phase 3 requires Phase 2** - APIs need Dataverse tables
4. **Phase 4 can start with 006** - Add-in UI work parallels API work
5. **Phase 5 requires Phase 3** - Workers use API patterns
6. **Phase 6 requires all prior** - Integration testing
7. **Phase 7 is final** - Production deployment
8. **Always end with 090** - Project wrap-up

---

## Critical Path

```
001 → 002 → (Phase 1 auth complete)
         ↘
003 → 006 → 040 → 045 → 049 → 056 → 057 → 070 → 078 → 080 → 081 → 090
         ↗
010 → 013 → 015 → 020 → 021 → 034 → 035 ↗
```

---

*Run `work on task 001` to begin implementation.*
