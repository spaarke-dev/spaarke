# Current Task State

> **Last Updated**: 2026-06-08 (active: scope extension — upload-indexing centralization)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Upload-indexing centralization (scope extension to multi-container-multi-index-r1) |
| **Active Phase** | Phase 1 — IPostUploadIndexingEnqueuer helper + DI + unit tests |
| **Status** | in progress |
| **Next Action** | Begin Phase 1 implementation (see checklist) |

### Resume protocol (after compaction or new session)

1. Read [`notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md`](./notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md) — checkboxes show exact state
2. Read [`notes/upload-indexing-centralization-design.md`](./notes/upload-indexing-centralization-design.md) — full spec, 11 fail-protections, 5 endpoints, Office/Outlook/Teams/SprkChat/SPA analysis
3. Resume at the first unchecked box in the checklist
4. Branch: `work/spaarke-multi-container-multi-index-r1`

### Critical context

The original multi-container-multi-index-r1 project shipped (40+ tasks). UAT revealed two issues:
- **Issue 1 (Matter wizard stale bundle)**: ✅ FIXED — `sprk_creatematterwizard` rebuilt + redeployed 2026-06-08 (1047 KB)
- **Issue 2 (architectural gap — Create* wizards never trigger RAG indexing)**: 🔄 IN PROGRESS — being fixed by this scope extension. Original Tier 3 indexer routing fix only wired DocumentUploadWizard's `/api/ai/rag/index-file` path. The other 4 wizards (Matter, Project, WorkAssignment, Event) upload to SPE via `EntityCreationService.uploadFilesToSpe` → `POST /api/spe/containers/.../upload` which never enqueued indexing.

**Also discovered during scope review** (and now in scope): `PersistDocumentAsync` on the SprkChat surface (`/api/ai/chat/sessions/{id}/documents/{docId}/persist`) has the same architectural gap.

### Fix architecture (single seam at the BFF)

Introduce `IPostUploadIndexingEnqueuer` server-side. Wire into 5 BFF upload endpoints. All clients (Create* wizards, External SPA, future Teams app) get indexing automatically — no client-side code change needed. Decommission DocumentUploadWizard's wizard-side `triggerRagIndexing` in Phase 4 cleanup.

Key references:
- BFF DI fix `2c9b9e73` deployed (BFF healthy, CORS works)
- Canonical pattern source: `UploadFinalizationWorker.EnqueueRagIndexingAsync` (Office Add-in)
- Existing resolver chain (`ISearchIndexNameResolver`) handles BU cascade + per-record override — unchanged

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | scope-ext-upload-indexing |
| **Task File** | [`notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md`](./notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md) |
| **Spec** | [`notes/upload-indexing-centralization-design.md`](./notes/upload-indexing-centralization-design.md) |
| **Title** | Centralized post-upload RAG indexing — server-side single seam |
| **Phase** | Phase 1 (Helper + DI + tests, no behavior change) |
| **Status** | in progress |

### Coordination

- **r6 parallel project**: `work/spaarke-ai-platform-unification-r6` modifies the same `Infrastructure/DI/AnalysisServicesModule.cs` file. Line-level changes don't directly overlap but require careful merge review before either project merges to master. See [`notes/handoffs/RESTART-bff-down-DI-fix-applied.md`](./notes/handoffs/RESTART-bff-down-DI-fix-applied.md).
- **Feature flag for emergency disable**: `Indexing:PostUploadEnqueueEnabled` (default `true`); set to `false` in App Service config + restart to disable indexing wholesale during incidents.

---

## Outcome Summary (original project — completed 2026-06-07)

- **43 tasks**: 40 ✅ + 1 🚫 deferred (Invoice wizard doesn't exist) + 3 🔲 UAT pending in-browser
- **Last commits**: see `git log work/spaarke-multi-container-multi-index-r1 --oneline` (latest at this scope extension: `2c9b9e73` DI fix + restart handoff)
- **Deploys to SPAARKE DEV 1**:
  - BFF: `spaarke-bff-dev` Azure App Service (45.5 MB; hash-verified; DI fix deployed 2026-06-08)
  - Wizards: 6 web resources published (CreateMatterWizard, CreateProjectWizard, CreateEventWizard, CreateWorkAssignmentWizard, DocumentUploadWizard, sprk_wizard_commands)
    - **`sprk_creatematterwizard` re-published 2026-06-08 09:43** with the matter-cascade alignment fix bundled (Issue 1 resolution)
  - PCF: SpaarkeSemanticSearch solution v1.1.74 imported + published (735 KB bundle)
  - Code page: sprk_semanticsearch web resource updated + published

## Outstanding (from original project — now superseded or partly handled by this scope extension)

- ~~**UAT 071-074**: in-browser verification~~ — superseded by scope-extension UAT in checklist Phase 3
- **AI Search indexer**: ✅ NOW IN SCOPE via this scope extension (was a separate follow-up)
- **Drift audit script bug**: 10-line fix to use entity-specific name attributes (documented in `notes/handoffs/053-backfill-dryrun.md`) — still pending
- **Backfill script param naming**: align `-Environment` vs `-EnvironmentUrl` (cosmetic) — still pending

---

## Files for Continuation

**Scope extension (active)**:
- [`notes/upload-indexing-centralization-design.md`](./notes/upload-indexing-centralization-design.md) — full design (canonical spec)
- [`notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md`](./notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md) — running tracker (the punch list)

**Project history**:
- [`README.md`](./README.md) — graduation-criteria status
- [`plan.md`](./plan.md) — phase outcomes
- [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) — original task-level status
- [`notes/lessons-learned.md`](./notes/lessons-learned.md) — comprehensive write-up of the original project
- [`notes/handoffs/RESTART-bff-down-DI-fix-applied.md`](./notes/handoffs/RESTART-bff-down-DI-fix-applied.md) — BFF DI fix context (still relevant for r6 coordination)
- [`notes/handoffs/post-uat-fixes-and-indexer-finding.md`](./notes/handoffs/post-uat-fixes-and-indexer-finding.md) — original bug-fix history
